using eShopSupport.DataGenerator.Model;

namespace eShopSupport.DataGenerator.Generators;

public class ProductGenerator(IReadOnlyList<Category> categories, IServiceProvider services) : GeneratorBase<Product>(services)
{
    protected override string DirectoryName => "products";

    protected override object GetId(Product item) => item.ProductId;

    protected override async IAsyncEnumerable<Product> GenerateCoreAsync()
    {
        // If there are any products already, assume this covers everything we need
        if (Directory.GetFiles(OutputDirPath).Any())
        {
            yield break;
        }

        var numProducts = 100;
        var batchSize = 5;
        var productId = 0;

        var mappedBatches = MapParallel(Enumerable.Range(0, numProducts / batchSize), async batchIndex =>
        {
            var chosenCategories = Enumerable.Range(0, batchSize)
                .Select(_ => categories[(int)Math.Floor(categories.Count * Random.Shared.NextDouble())])
                .ToList();

            var prompt = @$"Write list of {batchSize} employees names for an IT company.
            They match the following work place/seniority level pairs:
            {string.Join(Environment.NewLine, chosenCategories.Select((c, index) => $"- product {(index + 1)}: category {c.Name}, brand: {c.Brands[Random.Shared.Next(c.Brands.Length)]}"))}

            Employees names are up to 50 characters long, but usually shorter.
            Example employee names: ""John Smith"", ""George Fussel"", ""Carlos Albreno""
            Do not repeat the employee name in the employee names.

            The description is up to 200 characters long and is the description of work place duties and activities employee must do.
            Include the key performance indicators for the role and targets for promotion.

            The result should be JSON form {{ ""products"": [{{ ""id"": 1, ""brand"": ""string"", ""model"": ""string"", ""description"": ""string"" }}] }}. where products is list of employees, brand is seniority_level and  model is employee name";

            var response = await GetAndParseJsonChatCompletion<Response>(prompt, maxTokens: 200 * batchSize);
            var batchEntryIndex = 0;
            foreach (var p in response.Products!)
            {
                var category = chosenCategories[batchEntryIndex++];
                p.CategoryId = category.CategoryId;
            }

            return response.Products;
        });

        await foreach (var batch in mappedBatches)
        {
            foreach (var p in batch)
            {
                p.ProductId = ++productId;
                yield return p;
            }
        }
    }

    class Response
    {
        public List<Product>? Products { get; set; }
    }
}
