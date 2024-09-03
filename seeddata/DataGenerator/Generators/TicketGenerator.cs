using eShopSupport.DataGenerator.Model;

namespace eShopSupport.DataGenerator.Generators;

public class TicketGenerator(IReadOnlyList<Product> products, IReadOnlyList<Category> categories, IReadOnlyList<Manual> manuals, IServiceProvider services) : GeneratorBase<Ticket>(services)
{
    protected override string DirectoryName => "tickets/enquiries";

    protected override object GetId(Ticket item) => item.TicketId;

    protected override async IAsyncEnumerable<Ticket> GenerateCoreAsync()
    {
        // If there are any tickets already, assume this covers everything we need
        if (Directory.GetFiles(OutputDirPath).Any())
        {
            yield break;
        }

        var numTickets = 100;
        var batchSize = 10;
        var ticketId = 0;

        string[] situations = [
            "The Stellar Performer who consistently exceeds expectations, meets all goals, and contributes significantly to the team.",
            "The Struggling New Hire is having difficulty adjusting to their role. They might lack the necessary skills or experience.",
            "The Mid-Level Manager Seeking Promotion but does job poorly.",
            "The Collaborator Extraordinaire that excel at teamwork and collaboration and contribute to team cohesion.",
            "The Employee with Communication Challenges that struggles with clarity, verbosity, or misunderstandings.",
            "The Overcommitted Team Player that takes on too much, leading to burnout or subpar results.",
            "The Employee Who Missed Goals, despite effort, an employee falls short of their targets.",
            "The Employee with Unrealistic Self-Perception, someone overestimates their performance.",
            "The Employee Who Struggles with Feedback and disscussions. Employee becomes defensive or resistant during the meetings and presentations.",
            "The Manager’s Own Growth Review! Their performance review might focus on leadership.",
            "The client unhappy with employee performance.",
        ];

        string[] styles = [
            "polite",
            "extremely jovial, as if trying to be best friends",
            "formal",
            "embarassed and thinks they are the cause of their own problem",
            "not really interested in communicating clearly, only using a few words and assuming it can figure it out",
            "demanding and entitled",
            "frustrated and angry",
            "grumpy, and trying to claim there are bad employee",
            "extremely brief and abbreviated, by a teenager typing on a phone while distracted by another task",
            "extremely technical, as if trying to prove the superiority of their own knowledge",
            "relies on extremely, obviously false assumptions, but is earnest and naive",
            "providing almost no information, so it's impossible to know what they want or why they are submitting the support message",
        ];

        while (ticketId < numTickets)
        {
            var numInBatch = Math.Min(batchSize, numTickets - ticketId);
            var ticketsInBatch = await Task.WhenAll(Enumerable.Range(0, numInBatch).Select(async _ =>
            {
                var product = products[Random.Shared.Next(products.Count)];
                var category = categories.Single(c => c.CategoryId == product.CategoryId);
                var situation = situations[Random.Shared.Next(situations.Length)];
                var style = styles[Random.Shared.Next(styles.Length)];
                var manual = manuals.Single(m => m.ProductId == product.ProductId);
                var manualExtract = ManualGenerator.ExtractFromManual(manual);

                var prompt = @$"You are creating test data for a employees performance reviews system.
                    Write a performance review by a client, customer, collegue or manager who has worked with this employee:

                    Employee name: {product.Model}
                    Work place title: {category.Name}
                    Seniority level: {product.Brand}
                    Work place description: {product.Description}
                    Random extract from manual: <extract>{manualExtract}</extract>

                    The situation is: {situation}
                    If applicable, they can set if employee meet their expectations of was below or above expectations. However in most cases they
                    are giving full praise or describing particular situation or problem with that employee.

                    The Reviewer writes in the following style: {style}

                    Create a name for the author, writing the message as if you are that person. The Reviewer name
                    should be fictional and random, and should include relationship with employee in braces. Do not use cliched
                    or stereotypical names.

                    Where possible, the message should refer to something specific about this employee such as a job task
                    mentioned in its description or a fact mentioned in the manual (but the Reviewer does not refer
                    to having read the manual).

                    The message length may be anything from very brief (around 10 words) to very long (around 200 words).
                    Use blank lines for paragraphs if needed.

                    The result should be JSON form {{ ""customerFullName"": ""string"", ""message"": ""string"" }}. 
                    where customerFullName is author name";

                var ticket = await GetAndParseJsonChatCompletion<Ticket>(prompt);
                ticket.ProductId = product.ProductId;
                ticket.CustomerSituation = situation;
                ticket.CustomerStyle = style;
                return ticket;
            }));

            foreach (var t in ticketsInBatch)
            {
                t.TicketId = ++ticketId;
                yield return t;
            }
        }
    }
}
