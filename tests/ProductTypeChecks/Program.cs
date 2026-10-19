using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Papey.Data;
using Papey.Models;
using Papey.Services;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var connection = DatabaseConnection.Build(new ConfigurationBuilder().AddEnvironmentVariables().Build());
if (!new NpgsqlConnectionStringBuilder(connection).Database!.StartsWith("papey_type_review_"))
    throw new InvalidOperationException("Use a new disposable papey_type_review_* database.");
var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options;
await using var setup = new AppDbContext(options);
await setup.Database.MigrateAsync();
var empty = new ProductTypeEntity { Name = "تیشرت" };
var used = new ProductTypeEntity { Name = "Used" };
var racing = new ProductTypeEntity { Name = "Concurrent" };
setup.ProductTypes.AddRange(empty, used, racing);
var category = new Category { Name = "Preserve", Slug = "preserve", ProductType = empty };
setup.Categories.Add(category);
setup.Products.Add(new Product { Name = "Inactive product", Slug = "inactive", Price = 1, Category = category, ProductType = used, IsActive = false });
await setup.SaveChangesAsync();
int checks = 0;
void Check(bool ok, string description) { if (!ok) throw new Exception(description); checks++; Console.WriteLine("PASS: " + description); }
async Task<ProductTypeDeleteResult> Delete(int id) { await using var db = new AppDbContext(options); return await ProductTypeDeletion.DeleteIfEmptyAsync(db, id); }
Check(await Delete(used.Id) == ProductTypeDeleteResult.HasProducts, "Inactive products also prevent deleting their type");
Check(await Delete(empty.Id) == ProductTypeDeleteResult.Deleted, "An empty type can be deleted even if it has categories");
setup.ChangeTracker.Clear();
Check((await setup.Categories.SingleAsync()).ProductTypeId is null && await setup.Products.CountAsync() == 1,
    "Deleting a type preserves its categories and all products");
Check(await Delete(empty.Id) == ProductTypeDeleteResult.NotFound, "A repeated delete is handled safely");
await DbSeeder.EnsureStoreSeededAsync(setup);
Check(!await setup.ProductTypes.AnyAsync(t => t.Name == "تیشرت"), "Store startup does not recreate a deliberately deleted default type");
await using (var writer = new AppDbContext(options))
await using (var transaction = await writer.Database.BeginTransactionAsync())
{
    writer.Products.Add(new Product { Name = "New product", Slug = "concurrent", Price = 1, CategoryId = category.Id, ProductTypeId = racing.Id });
    await writer.SaveChangesAsync();
    var deletion = Delete(racing.Id);
    await Task.Delay(200);
    Check(!deletion.IsCompleted, "Deletion waits for an in-flight product reference");
    await transaction.CommitAsync();
    Check(await deletion == ProductTypeDeleteResult.HasProducts, "A concurrent new product prevents deletion after commit");
}
Check(await setup.Products.CountAsync() == 2 && await setup.ProductTypes.AnyAsync(t => t.Id == racing.Id), "Concurrent deletion never detaches or deletes products");
Console.WriteLine($"{checks} product type checks passed.");
