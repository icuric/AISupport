using System.Text;
using System.Threading.Channels;
using eShopSupport.DataGenerator.Model;

namespace eShopSupport.DataGenerator.Generators;

public class EvalQuestionGenerator(IReadOnlyList<Product> products, IReadOnlyList<Category> categories, IReadOnlyList<Manual> manuals, IServiceProvider services)
    : GeneratorBase<EvalQuestion>(services)
{
    protected override string DirectoryName => "evalquestions";

    protected override object GetId(EvalQuestion item)
        => item.QuestionId;

    protected override async IAsyncEnumerable<EvalQuestion> GenerateCoreAsync()
    {
        // If there are any questions already, assume this covers everything we need
        if (Directory.GetFiles(OutputDirPath).Any())
        {
            yield break;
        }

        var numQuestions = 500;
        var questionId = 0;
        var outputChannel = Channel.CreateUnbounded<EvalQuestion>();
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 10 };
        CompleteOutputAfterTask(outputChannel.Writer, Parallel.ForAsync(0, numQuestions, parallelOptions, async (_, _) =>
        {
            var item = await GenerateSingle();
            if (!string.IsNullOrWhiteSpace(item.Question) && !string.IsNullOrEmpty(item.Answer))
            {
                item.QuestionId = Interlocked.Increment(ref questionId);
                await outputChannel.Writer.WriteAsync(item);
            }
        }));

        await foreach (var item in outputChannel.Reader.ReadAllAsync())
        {
            yield return item;
        }
    }

    private static void CompleteOutputAfterTask<T>(ChannelWriter<T> writer, Task task)
    {
        Task.Run(async () =>
        {
            try
            {
                await task;
                writer.Complete();
            }
            catch (Exception ex)
            {
                writer.Complete(ex);
            }
        });
    }

    private async Task<EvalQuestion> GenerateSingle()
    {
        var product = products[Random.Shared.Next(products.Count)];
        var category = categories.Single(c => c.CategoryId == product.CategoryId);
        var manual = manuals.Single(m => m.ProductId == product.ProductId);
        var manualExtract = ManualGenerator.ExtractFromManual(manual);
        var isQuestionWrittenByAgent = Random.Shared.NextDouble() < 0.75;
        var questionPrompt = isQuestionWrittenByAgent
            ? """
                    Questions are short, typically 3-6 words, and are not always full sentences. They may look
                    like search queries or things typed in a hurry by a support agent. They are not polite or
                    verbose, since they are addressed to a machine.
                    Example questions might be "not working", "what are the employee responsibilities", 
                    "what are key performance indicators for the role and targets for promotion",
                    "what are work place duties and activities employee must do?"
                    """
            : """
                    The question is actually an entire email written by a customer. It usually starts with a
                    greeting, some description of their situation, and then their question. The whole thing
                    may be up to 100 words long. It may contain spelling and grammar errors, and may be angry
                    or rude.
                    """;

        var question = await GetAndParseJsonChatCompletion<EvalQuestion>($$"""
                    There is an AI system used by HR assistant agents working for an IT company.
                    The AI system is used to collect reviews about employee performance and to help agents analize reviews.

                    Your task is to write question/answer pairs that will be used to evaluate the
                    performance of that AI system. All the questions you write will be about actual employees
                    working in that company, based on information from the employee catalog and manuals. The
                    questions should plausibly represent what Reviewer and HR agents will ask.

                    In this case, you are to write a question/answer pair based on the following context:

                    <employee_name>{{product.Model}}</employee_name>
                    <seniority_level>{{product.Brand}}</seniority_level>
                    <work_place>{{category.Name}}</work_place>
                    <extract_from_manual>{{manualExtract}}</extract_from_manual>

                    Questions are one of the following types:
                     - A question to help a Reviewer who wants to know about employee responsibilities or other objective facts
                     - A question to help a Reviewer resolve an issue with employee

                    You must select an OBJECTIVE FACT from the manual and write a question to which
                    that fact is the answer. Only select facts that are distinctive about this specific work place,
                    not generic information about any work place.

                    Always follow these style guidelines:
                     - {{questionPrompt}}
                     - Answers are short, typically a single brief sentence of 1-10 words. Never use more than
                       20 words for an answer.
                     - The "verbatim_quote_from_manual" is 3-6 words taken EXACTLY from the manual which are
                       the factual basis for the question and asnwer.
                     - If the provided context does not contain a suitable fact, set all the response properties
                       to null or empty strings.

                    Respond as JSON in the following form: {
                        "question": "string",
                        "answer": "string",
                        "verbatimQuoteFromManual": "string"
                    }
                    """);
        question.ProductId = product.ProductId;
        return question;
    }
}
