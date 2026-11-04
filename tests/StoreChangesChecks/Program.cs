using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using Papey.Data;
using Papey.Models;
using Papey.Services;
int count=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;Console.WriteLine("PASS: "+message);}
var conn=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PAPEY_TEST_CONNECTION")??throw new Exception("Disposable DB required")){Pooling=false};var database="papey_store_changes_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(new NpgsqlConnectionStringBuilder(conn.ConnectionString){Database="postgres"}.ConnectionString);await admin.OpenAsync();await new NpgsqlCommand($"CREATE DATABASE \"{database}\"",admin).ExecuteNonQueryAsync();conn.Database=database;var factory=new Factory(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(conn.ConnectionString).Options);var root=Path.Combine(Path.GetTempPath(),"papey-guide-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try{
 await using var db=factory.CreateDbContext();await db.GetService<IMigrator>().MigrateAsync("20261028110152_CustomDesignAdminReplies");
 await db.Database.ExecuteSqlRawAsync("INSERT INTO \"ProductTypes\" (\"Name\",\"DisplayOrder\",\"IsActive\") VALUES ('تیشرت',0,true),('هودی',1,true),('لانگ اسلیو',2,true)");await db.Database.MigrateAsync();
 var types=await db.ProductTypes.OrderBy(t=>t.DisplayOrder).ToListAsync();Check(types[0].SizeGuideImageUrl==ProductSizeGuides.ShirtImage&&types[1].SizeGuideImageUrl==ProductSizeGuides.HoodieImage&&types[2].SizeGuideImageUrl==ProductSizeGuides.LongSleeveImage,"Migration brings all three existing guides into type settings");
 var owner=new AppUser{Username="owner",IsAdmin=true,PasswordHash="unused"};var seller=new AppUser{Username="seller",IsStaff=true,PasswordHash="unused"};var customer=new AppUser{Username="customer",PasswordHash="unused"};db.Users.AddRange(owner,seller,customer);await db.SaveChangesAsync();
 var uploads=new ProductDraftUploads(new Env{ContentRootPath=root,WebRootPath=Path.Combine(root,"wwwroot")},TimeProvider.System);var guides=new SizeGuideSettings(factory,uploads);
 Check(await guides.CanManageAsync(AccountAccess.Principal(owner))&&!await guides.CanManageAsync(AccountAccess.Principal(seller))&&!await guides.CanManageAsync(AccountAccess.Principal(customer)),"Only owner can manage size guide settings");
 Check(await guides.ReadAsync(types[1].Id,null)==ProductSizeGuides.HoodieImage,"Product page reads its type-specific guide");
 var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAJUlEQVR4nGO8syrgPwMFgIkSzaMGQAATA4WAadQAhtEwYKA8DAC/6QL1RvCsJAAAAABJRU5ErkJggg==");using var input=new MemoryStream(png);var staged=await uploads.StageAsync(input,"guide.png");
 Check(!await guides.SaveAsync(AccountAccess.Principal(seller),types[1].Id,staged),"Seller cannot replace a type guide through the service");
 Check(await guides.SaveAsync(AccountAccess.Principal(owner),types[1].Id,staged),"Owner can save a new type guide");var saved=await guides.ReadAsync(types[1].Id,null);
 Check(saved.StartsWith("/uploads/images/")&&File.Exists(Path.Combine(root,"wwwroot",saved.TrimStart('/').Replace('/',Path.DirectorySeparatorChar))),"Uploaded guide persists in the shared image store");
 Check(await guides.ReadAsync(null,"هودی")==saved,"Custom clothing studio uses the same configured guide");Check(await guides.ReadAsync(types[0].Id,null)==ProductSizeGuides.ShirtImage,"Changing a hoodie guide preserves other clothing guides");
 var request=new CustomDesignRequest{Garment="hoodie",Colour="white",Size="XL",Quantity=2};var url=CustomDesignCatalog.WhatsAppUrl(request);var text=Uri.UnescapeDataString(new Uri(url).Query[6..]);
 Check(url.StartsWith("https://wa.me/989036421161?text=")&&text.Contains(request.Number)&&text.Contains("سلام")&&text.Contains("XL"),"WhatsApp handoff pre-fills greeting, request reference and garment details");

 var category=new Category{Name="Photo category",Slug="photo-test",ProductTypeId=types[1].Id};db.Categories.Add(category);await db.SaveChangesAsync();
 db.Products.AddRange(new Product{Name="Real photo",Slug="real-photo",CategoryId=category.Id,ProductTypeId=types[1].Id,ImageUrl="/uploads/images/real-photo.jpg",Price=100,HomeHeroSlot=1},new Product{Name="Hidden photo",Slug="hidden-photo",CategoryId=category.Id,ProductTypeId=types[1].Id,ImageUrl="/uploads/images/hidden.jpg",Price=100,IsActive=false});await db.SaveChangesAsync();
 var photos=new ProductTypePhotos(factory);Check(await photos.ReadAsync(types[1].Id)=="/uploads/images/real-photo.jpg","Type selector uses an active product photograph and ignores hidden products");
 Check(await photos.ReadAsync(999999)==null,"Empty product types allow the icon fallback");
 Check(!db.Database.HasPendingModelChanges(),"Size guide migration matches model");Console.WriteLine($"{count} store settings and handoff checks passed.");
}finally{await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)",admin).ExecuteNonQueryAsync();if(Path.GetDirectoryName(root)==Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)&&Path.GetFileName(root).StartsWith("papey-guide-tests-"))Directory.Delete(root,true);}
sealed class Factory(DbContextOptions<AppDbContext> options):IDbContextFactory<AppDbContext>{public AppDbContext CreateDbContext()=>new(options);}
sealed class Env:IWebHostEnvironment{public string EnvironmentName{get;set;}="Production";public string ApplicationName{get;set;}="Checks";public string ContentRootPath{get;set;}="";public string WebRootPath{get;set;}="";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();}
