using eShopSupport.DataGenerator.Model;
using System.Text.RegularExpressions;

namespace eShopSupport.DataGenerator.Generators;

public class CategoryGenerator(IServiceProvider services) : GeneratorBase<Category>(services)
{
    protected override string DirectoryName => "categories";

    protected override object GetId(Category item) => item.CategoryId;

    protected override async IAsyncEnumerable<Category> GenerateCoreAsync()
    {
        // If there are any categories already, assume this covers everything we need
        if (Directory.GetFiles(OutputDirPath).Any())
        {
            yield break;
        }

        var numCategories = 50;
        var batchSize = 25;
        var categoryNames = new HashSet<string>();

        while (categoryNames.Count < numCategories)
        {
            Console.WriteLine($"Generating {batchSize} categories...");

            var prompt = @$"Generate {batchSize} employee work place title for an IT company offering various high-tech services, e.g. ""Software developer"", ""Quality Assurance"".

            Each work place title has a list of 4 seniority levels in that category, namely
            ""Junior"", ""Specialist"", ""Senior"", ""Principal"", ""Lead"".
            
            The response should be a JSON object of the form {{ ""categories"": [{{""name"":""Software developer"", ""brands"":[""Junior"", ""Specialist"", ""Senior"", ""Principal"", ""Lead""]}}, ...] }}.";

            var response = await GetAndParseJsonChatCompletion<Response>(prompt, maxTokens: 70 * batchSize);
            foreach (var c in response.Categories)
            {
                if (categoryNames.Add(c.Name))
                {
                    c.CategoryId = categoryNames.Count;
                    c.Brands = c.Brands;
                    yield return c;
                }
            }
        }
    }

    private static string ImproveBrandName(string name)
    {
        // Almost invariably, the name is a PascalCase word like AquaTech, even though we told it to use spaces.
        // For variety, convert to "Aqua Tech" or "Aquatech" sometimes.
        return Regex.Replace(name, @"([a-z])([A-Z])", m => Random.Shared.Next(3) switch
        {
            0 => $"{m.Groups[1].Value} {m.Groups[2].Value}",
            1 => $"{m.Groups[1].Value}{m.Groups[2].Value.ToLower()}",
            _ => m.Value
        });
    }

    private class Response
    {
        public required List<Category> Categories { get; set; }
    }
}
