using eShopSupport.DataGenerator.Model;
using Microsoft.SemanticKernel;
using System.Text;
using System.ComponentModel;
using Microsoft.SemanticKernel.Text;
using Microsoft.SemanticKernel.Embeddings;
using SmartComponents.LocalEmbeddings.SemanticKernel;
using System.Numerics.Tensors;

namespace eShopSupport.DataGenerator.Generators;

public class TicketThreadGenerator(IReadOnlyList<Ticket> tickets, IReadOnlyList<Product> products, IReadOnlyList<Category> categories, IReadOnlyList<Manual> manuals, IServiceProvider services) : GeneratorBase<TicketThread>(services)
{
    private readonly ITextEmbeddingGenerationService embedder = new LocalTextEmbeddingGenerationService();

    protected override object GetId(TicketThread item) => item.TicketId;

    protected override string DirectoryName => "tickets/threads";

    protected override IAsyncEnumerable<TicketThread> GenerateCoreAsync()
    {
        // Skip the ones we already have
        var threadsToGenerate = tickets.Where(t => !File.Exists(GetItemOutputPath(t.TicketId.ToString()))).ToList();
        return MapParallel(threadsToGenerate, GenerateThreadAsync);
    }

    private async Task<TicketThread> GenerateThreadAsync(Ticket ticket)
    {
        var messageId = 0;
        var thread = new TicketThread
        {
            TicketId = ticket.TicketId,
            ProductId = ticket.ProductId,
            CustomerFullName = ticket.CustomerFullName,
            Messages = [new TicketThreadMessage { AuthorRole = Role.Customer, MessageId = ++messageId, Text = ticket.Message }]
        };
        var product = products.Single(p => p.ProductId == ticket.ProductId);
        var category = categories.Single(c => c.CategoryId == product.CategoryId);

        // Assume there's a 1-in-3 chance that any message is the last one in the thread
        // (including the first one, so we might not need to generate any more).
        // So the number of messages in a thread is geometrically distributed with p = 1/3.
        const double p = 1.0 / 3.0;
        var maxMessagesInThread = Math.Floor(Math.Log(1 - Random.Shared.NextDouble()) / Math.Log(1 - p));

        for (var i = 0; i < maxMessagesInThread; i++)
        {
            var lastMessageRole = thread.Messages.Last().AuthorRole;
            var messageRole = lastMessageRole switch
            {
                Role.Customer => Role.Assistant,
                Role.Assistant => Role.Customer,
                _ => throw new NotImplementedException(),
            };

            var response = messageRole switch
            {
                Role.Customer => await GenerateCustomerMessageAsync(product, category, ticket, thread.Messages),
                Role.Assistant => await GenerateAssistantMessageAsync(product, ticket, thread.Messages, manuals),
                _ => throw new NotImplementedException(),
            };

            thread.Messages.Add(new TicketThreadMessage { MessageId = ++messageId, AuthorRole = messageRole, Text = response.Message });

            if (response.ShouldClose)
            {
                break;
            }
        }

        return thread;
    }

    private async Task<Response> GenerateCustomerMessageAsync(Product product, Category category, Ticket ticket, IReadOnlyList<TicketThreadMessage> messages)
    {
        var prompt = $@"You are generating test data for a employees performance reviews system. There is an open review as follows:
        
        Employee name: {product.Model}
        Work place title: {category.Name}
        Seniority level: {product.Brand}
        Work place description: {product.Description}

        Reviewer name: {ticket.CustomerFullName}

        The message log so far is:

        {FormatMessagesForPrompt(messages)}

        Generate the next reply from the Reviewer. You may do any of:

        - Supply more information about the situation as requested by the HR agent
        - Write more detailed review as previous one was not good enough
        - Confirm that you are satisfied with performance of employee and that he Meets expectations
        - Complain about the employee and say that his performance is Below expectations
        - Say you are extatic about employee performance and that he is Above expectations

        Write as if you are the Reviewer. This Reviewer ALWAYS writes in the following style: {ticket.CustomerStyle}.

        Respond in the following JSON format: {{ ""message"": ""string"", ""shouldClose"": bool }}.
        Indicate that the review should be closed if, as the Reviewer, you feel the review is resolved and completed (whether or not you are satisfied).
";

        return await GetAndParseJsonChatCompletion<Response>(prompt);
    }

    private async Task<Response> GenerateAssistantMessageAsync(Product product, Ticket ticket, IReadOnlyList<TicketThreadMessage> messages, IReadOnlyList<Manual> manuals)
    {
        var prompt = $@"You are a Human resources agent working for IT company. You are responding to a Reviewer
        performance review about the following employee:

        Employee name: {product.Model}
        Seniority level: {product.Brand}

        The message log so far is:

        {FormatMessagesForPrompt(messages)}

        Your job is to provide the next message to send to the Reviewer, and ideally close the review. 
        Your goal is to help complete their review, which might include:

        - Requesting detailed informations if Reviewer finds employee below expectations or if some incident happened
        - Recommending a future steps to mitigate complains and problems if Reviewer finds employee below expectations
        - Requesting information on how employee can be even better if he only Meets expectations and how to achive better results
        - Expressing delight that employee performance level is Above expectations

        You must first decide if you have enough information, and if not, either ask the Reviewer for more details or search for information
        in the Job Systematization manual using the configured tool. Don't repeat information that was already given earlier in the message log.

        You ONLY give information based on the Employee details and manual. If you cannot answer based on the provided context, say that you don't know.
        Whenever possible, give your answer as a quote from the manual, for example saying ""According to the manual, ..."".

        You refer to yourself only as ""HR Assistant"", or ""HR Team"".

        Respond in the following JSON format: {{ ""message"": ""string"", ""shouldClose"": bool }}.
        Indicate that the review should be closed only if the Reviewer has confirmed it is resolved.
        It's OK to give very short, 1-sentence replies if applicable.
        ";

        var manual = manuals.Single(m => m.ProductId == product.ProductId);
        var tools = new AssistantTools(embedder, manual);

        return await GetAndParseJsonChatCompletion<Response>(prompt, tools: tools);
    }

    public static string FormatMessagesForPrompt(IReadOnlyList<TicketThreadMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var message in messages)
        {
            sb.AppendLine($"<message role=\"{message.AuthorRole}\">{message.Text}</message>");
        }
        return sb.ToString();
    }

    private class Response
    {
        public required string Message { get; set; }
        public bool ShouldClose { get; set; }
    }

    private class AssistantTools(ITextEmbeddingGenerationService embedder, Manual manual)
    {
        [KernelFunction, Description("Searches for information in the product's user manual.")]
        public async Task<string> SearchUserManualAsync([Description("text to look for in user manual")] string query)
        {
            // Obviously it would be more performant to chunk and embed each manual only once, but this is simpler for now
            var chunks = TextChunker.SplitPlainTextParagraphs([manual.MarkdownText], 100);
            var embeddings = await embedder.GenerateEmbeddingsAsync(chunks);
            var candidates = chunks.Zip(embeddings);
            var queryEmbedding = await embedder.GenerateEmbeddingAsync(query);

            var closest = candidates
                .Select(c => new { Text = c.First, Similarity = TensorPrimitives.CosineSimilarity(c.Second.Span, queryEmbedding.Span) })
                .OrderByDescending(c => c.Similarity)
                .Take(3)
                .Where(c => c.Similarity > 0.6f)
                .ToList();

            if (closest.Any())
            {
                return string.Join(Environment.NewLine, closest.Select(c => $"<snippet_from_manual>{c.Text}</snippet_from_manual>"));
            }
            else
            {
                return "The manual contains no relevant information about this";
            }
        }
    }
}
