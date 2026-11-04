using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Npgsql;
using Papey.Data;
using Papey.Models;
using Papey.Services;

var checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS: "+name);}
async Task Reject(Func<Task> action,string name){try{await action();}catch(ArgumentException){Check(true,name);return;}catch(UnauthorizedAccessException){Check(true,name);return;}throw new Exception(name);}
var source=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PAPEY_TEST_CONNECTION")??throw new Exception("Disposable DB required")){Pooling=false};var database="papey_custom_checks_"+Guid.NewGuid().ToString("N");
await using var connection=new NpgsqlConnection(new NpgsqlConnectionStringBuilder(source.ConnectionString){Database="postgres"}.ConnectionString);await connection.OpenAsync();await new NpgsqlCommand($"CREATE DATABASE \"{database}\"",connection).ExecuteNonQueryAsync();source.Database=database;
var factory=new Factory(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source.ConnectionString).Options);
var root=Path.Combine(Path.GetTempPath(),"papey-custom-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Process? server=null;
var bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAJUlEQVR4nGO8syrgPwMFgIkSzaMGQAATA4WAadQAhtEwYKA8DAC/6QL1RvCsJAAAAABJRU5ErkJggg==");
var env=new TestEnvironment{ContentRootPath=root};var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["SMSIR_API_KEY"]="test-only-fake-key",["SMSIR_LINE_NUMBER"]="1000000000",["ORDER_NOTIFICATION_PHONES"]="09120000001,09120000002"}).Build();var fake=new FakeSms();var sms=new SmsService(new HttpClient(fake),config,env,NullLogger<SmsService>.Instance);var service=new CustomDesignService(factory,env,sms);
try{
 await using var db=factory.CreateDbContext();await db.Database.MigrateAsync();
 const string password="Disposable-test-password-123";
 var buyer=new AppUser{Username="design-buyer",FullName="Design buyer",PasswordHash=BCrypt.Net.BCrypt.HashPassword(password)};var other=new AppUser{Username="other",PasswordHash="unused"};var staff=new AppUser{Username="staff",IsStaff=true,PasswordHash="unused"};var owner=new AppUser{Username="owner",IsAdmin=true,PasswordHash=BCrypt.Net.BCrypt.HashPassword(password)};db.Users.AddRange(buyer,other,staff,owner);await db.SaveChangesAsync();var principal=AccountAccess.Principal(buyer);
 var layers=new List<DesignLayer>{new(0,"front",.1,.1,.4,.4,0),new(0,"back",.2,.2,.5,.5,0)};
 FormCollection Form(Guid? key=null,byte[]? content=null,string garment="tshirt",string quantity="2")=>new(new Dictionary<string,StringValues>{["key"]=(key??Guid.NewGuid()).ToString(),["name"]="Test Buyer",["phone"]="09120000003",["garment"]=garment,["colour"]="white",["size"]="XL",["quantity"]=quantity,["notes"]="front and back design",["layers"]=JsonSerializer.Serialize(layers)},new FormFileCollection{new FormFile(new MemoryStream(content??bytes),0,(content??bytes).Length,"files","untrusted.png"){Headers=new HeaderDictionary(),ContentType="image/png"}});

 var sleeve=new DesignLayer(0,"front",.04,.48,.12,.12,0,2);var hem=new DesignLayer(0,"back",.4,.8,.12,.1,0,2);
 Check(CustomDesignService.ValidLayers([sleeve,hem],1),"Full canvas permits sleeve and hem positions");
 Check(CustomDesignCatalog.StagePlacement("hoodie",sleeve)==sleeve&&CustomDesignCatalog.StagePlacement("tshirt",hem)==hem,"Saved full-canvas layouts retain exact placement");
 var legacy=CustomDesignCatalog.StagePlacement("hoodie",layers[1]);Check(Math.Abs(legacy.X-.374)<.00001&&Math.Abs(legacy.Y-.414)<.00001&&Math.Abs(legacy.Width-.21)<.00001&&Math.Abs(legacy.Height-.135)<.00001,"Legacy chest-only request previews remain unchanged");
 Check(!CustomDesignService.ValidLayers([sleeve with{CanvasVersion=99}],1),"Unknown coordinate versions are rejected");

 Check(CustomDesignService.ValidLayers([sleeve with{Rotation=45},hem with{Rotation=-90}],1),"Valid clockwise and counterclockwise rotations accepted");
 Check(!CustomDesignService.ValidLayers([sleeve with{Rotation=double.NaN}],1)&&!CustomDesignService.ValidLayers([sleeve with{Rotation=181}],1),"Nonfinite and out-of-range rotations rejected");
 Check(!CustomDesignService.ValidLayers([sleeve with{X=0,Y=0,Rotation=45}],1),"Rotated artwork must stay inside the saved canvas");
 Check(CustomDesignCatalog.StagePlacement("hoodie",sleeve with{Rotation=37}).Rotation==37,"Admin preview preserves stored rotation");
 Check(CustomDesignService.ValidPng(bytes),"Valid PNG passes envelope and CRC validation");var corrupt=bytes.ToArray();corrupt[50]^=1;Check(!CustomDesignService.ValidPng(corrupt)&&!CustomDesignService.ValidPng([1,2,3])&&!CustomDesignService.ValidPng(bytes.Concat(new byte[]{1}).ToArray()),"Corrupted, disguised and appended PNGs rejected");
 Check(CustomDesignService.ValidLayers(layers,1),"One PNG can appear on both sides");Check(!CustomDesignService.ValidLayers([layers[0] with{Width=double.NaN}],1)&&!CustomDesignService.ValidLayers([layers[0] with{X=.9}],1)&&!CustomDesignService.ValidLayers([layers[0] with{Side="outside"}],1)&&!CustomDesignService.ValidLayers(layers,2),"Coordinates, side, file references validated");
 await Reject(()=>service.SubmitAsync(new ClaimsPrincipal(),Form(),default),"Anonymous submission denied");await Reject(()=>service.SubmitAsync(principal,Form(garment:"unknown"),default),"Unknown garment denied");await Reject(()=>service.SubmitAsync(principal,Form(quantity:"0"),default),"Invalid quantity denied");await Reject(()=>service.SubmitAsync(principal,Form(content:corrupt),default),"Invalid PNG upload denied");Check(!Directory.EnumerateDirectories(Path.Combine(root,"App_Data","custom-designs")).Any(),"Rejected upload removed from private storage");
 var key=Guid.NewGuid();var result=await service.SubmitAsync(principal,Form(key),default);Check(File.ReadAllBytes(Path.Combine(service.Folder(result.Id),"0.png")).SequenceEqual(bytes),"Original PNG preserved privately");Check((await service.SubmitAsync(principal,Form(key),default)).Id==result.Id&&await db.CustomDesignRequests.CountAsync()==1,"Retry does not create duplicate request");
 var parallelKey=Guid.NewGuid();var concurrent=await Task.WhenAll(Enumerable.Range(0,3).Select(_=>service.SubmitAsync(principal,Form(parallelKey),default)));Check(concurrent.Select(r=>r.Id).Distinct().Count()==1,"Concurrent duplicate requests are idempotent");
 Check(await db.Orders.CountAsync()==0&&await db.OrderPrintJobs.CountAsync()==0,"No payment, sale or print job created");Check(await db.CustomDesignSmsJobs.CountAsync(j=>j.RequestId==result.Id)==2&&!await db.CustomDesignSmsJobs.AnyAsync(j=>j.Phone=="09120000003"),"Only configured admin recipients notified");
 Check(await service.MayReadAsync(principal,result.Id,default)&&await service.MayReadAsync(AccountAccess.Principal(staff),result.Id,default)&&await service.MayReadAsync(AccountAccess.Principal(owner),result.Id,default)&&!await service.MayReadAsync(AccountAccess.Principal(other),result.Id,default),"Only buyer and active admins can read files");
 Check(!await service.UpdateAsync(principal,result.Id,CustomDesignStatus.Contacted,"fake reply","",default),"Customer cannot publish an admin response");
 Check(await service.UpdateAsync(AccountAccess.Principal(staff),result.Id,CustomDesignStatus.Contacted,"Staff response","PRIVATE-WORKSHOP-NOTE",default),"Support staff can respond to a custom request");
 Check(!await service.UpdateAsync(AccountAccess.Principal(owner),result.Id,CustomDesignStatus.Contacted,new string('x',2001),"",default),"Oversized response is rejected");
 Check(await service.UpdateAsync(AccountAccess.Principal(owner),result.Id,CustomDesignStatus.Agreed,"Customer-visible quote","PRIVATE-WORKSHOP-NOTE",default),"Owner can save a customer-visible quote separately from internal notes");
 var answered=await db.CustomDesignRequests.AsNoTracking().SingleAsync(r=>r.Id==result.Id);Check(answered.AdminReply=="Customer-visible quote"&&answered.AdminNotes=="PRIVATE-WORKSHOP-NOTE"&&answered.AdminRepliedAt!=null&&answered.Status==CustomDesignStatus.Agreed,"Reply, private notes, timestamp and status persist independently");
 Check(await db.CustomDesignSmsJobs.CountAsync(j=>j.RequestId==result.Id)==2,"Saving a response does not resend admin notifications");
 var old=AccountAccess.Principal(staff);staff.IsDisabled=true;staff.AuthVersion++;await db.SaveChangesAsync();Check(!await service.MayReadAsync(old,result.Id,default),"Disabled staff loses file access immediately");
 var queue=new CustomDesignSmsQueue(factory,sms);Check(await queue.ProcessOneAsync(),"Persistent SMS queue processes a notification");Check(fake.Texts.Last().Contains("طرح اختصاصی")&&!fake.Texts.Last().Contains("پرداخت‌شده"),"SMS describes custom request without claiming payment");Check(await db.CustomDesignSmsJobs.AnyAsync(j=>j.State==ShippingSmsState.Accepted&&j.MessageId==123456),"Provider acceptance and ID stored");fake.FailUncertain=true;await queue.ProcessOneAsync();Check(await db.CustomDesignSmsJobs.AnyAsync(j=>j.State==ShippingSmsState.Unknown),"Ambiguous SMS held for inspection instead of duplicate retry");fake.FailUncertain=false;
 await service.SubmitAsync(principal,Form(),default);await service.SubmitAsync(principal,Form(),default);await service.SubmitAsync(principal,Form(),default);await Reject(()=>service.SubmitAsync(principal,Form(),default),"Daily account request limit enforced");Check(!db.Database.HasPendingModelChanges(),"Migration matches current model");
 var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
 var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(Path.GetFullPath("bin/Debug/net10.0/Papey.dll"));start.ArgumentList.Add("--urls");start.ArgumentList.Add($"http://127.0.0.1:{port}");start.ArgumentList.Add("--contentRoot");start.ArgumentList.Add(root);
 foreach(var pair in new Dictionary<string,string>{["ConnectionStrings__DefaultConnection"]=source.ConnectionString,["ASPNETCORE_ENVIRONMENT"]="Production",["Deployment__HttpsOnly"]="false",["Deployment__RequireSecureConfiguration"]="false",["DataProtection__KeyPath"]=Path.Combine(root,"keys"),["DataProtection__ApplicationName"]="Papey.CustomDesignChecks",["ADMIN_USERNAME"]="owner",["ADMIN_PASSWORD"]=password,["SMSIR_API_KEY"]="YOUR_DISABLED_TEST_KEY",["SMSIR_SANDBOX_API_KEY"]="YOUR_DISABLED_TEST_KEY",["SMSIR_SANDBOX"]="true",["ORDER_NOTIFICATION_PHONES"]=""})start.Environment[pair.Key]=pair.Value;
 server=Process.Start(start)!;var logs=server.StandardOutput.ReadToEndAsync();var errors=server.StandardError.ReadToEndAsync();using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri($"http://127.0.0.1:{port}")};
 for(var attempt=0;attempt<100;attempt++){try{if((await client.GetAsync("/healthz")).IsSuccessStatusCode)break;}catch{}if(server.HasExited)throw new Exception("Test server failed: "+await errors);await Task.Delay(200);}
 Check((await client.GetStringAsync("/custom-design")).Contains("callback-form"),"Public callback form renders without login");Check((await client.PostAsync("/api/custom-designs",new MultipartFormDataContent())).StatusCode==HttpStatusCode.Unauthorized,"Anonymous API returns 401");
 var login=await client.PostAsync("/account/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"identifier",buyer.Username},{"password",password}}));Check(login.StatusCode==HttpStatusCode.Redirect,"Customer signs in");var page=await client.GetStringAsync("/custom-design");Check(page.Contains("callback-phone")&&!page.Contains("studio-stage"),"Customers see the simplified phone-only custom request");Check((await client.PostAsync("/api/custom-designs",new MultipartFormDataContent())).StatusCode==HttpStatusCode.BadRequest,"Missing antiforgery token rejected");
 var token=Regex.Match(page,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");Check(token.Success,"Studio provides antiforgery token");
 await db.CustomDesignRequests.Where(r=>r.UserId==buyer.Id).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.CreatedAt,DateTime.UtcNow.AddDays(-2)));
 using var multipart=new MultipartFormDataContent();var apiKey=Guid.NewGuid();foreach(var field in new Dictionary<string,string>{{"__RequestVerificationToken",WebUtility.HtmlDecode(token.Groups[1].Value)},{"key",apiKey.ToString()},{"name","HTTP Test"},{"phone","09120000003"},{"garment","hoodie"},{"colour","grey"},{"size","L"},{"quantity","1"},{"notes","local test only"},{"layers",JsonSerializer.Serialize(layers.Select(l=>l with{Rotation=30,CanvasVersion=2}))}})multipart.Add(new StringContent(field.Value),field.Key);var png=new ByteArrayContent(bytes);png.Headers.ContentType=new("image/png");multipart.Add(png,"files","test.png");
 using var submitted=await client.PostAsync("/api/custom-designs",multipart);Check(submitted.IsSuccessStatusCode,"Valid multipart submission with antiforgery succeeds");var saved=await db.CustomDesignRequests.SingleAsync(r=>r.SubmissionKey==apiKey);Check(saved.Garment=="hoodie"&&saved.Colour=="grey"&&File.Exists(Path.Combine(service.Folder(saved.Id),"0.png")),"HTTP request preserves clothing options and original PNG");
 Check(JsonSerializer.Deserialize<List<DesignLayer>>(saved.LayersJson)!.All(l=>l.Rotation==30&&l.CanvasVersion==2),"Real HTTP submission persists rotated layouts");
 var download=await client.GetAsync($"/api/custom-designs/{saved.Id}/files/0?download=true");Check(download.IsSuccessStatusCode&&download.Headers.CacheControl?.NoStore==true&&download.Content.Headers.ContentDisposition?.DispositionType=="attachment"&&(await download.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes),"Private download is intact, attached and no-store");
 Check(!(await client.GetAsync("/admin/custom-designs")).IsSuccessStatusCode,"Customer denied admin list");var history=await client.GetStringAsync("/account/custom-designs");Check(history.Contains(result.Number),"Customer history includes saved request");Check(history.Contains("rotate(30deg)"),"Customer history renders the saved image rotation");Check(history.Contains("Customer-visible quote")&&!history.Contains("PRIVATE-WORKSHOP-NOTE"),"Customer history shows the admin response and never exposes internal notes");
 using var manager=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=client.BaseAddress};await manager.PostAsync("/account/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"identifier","owner"},{"password",password}}));Check((await manager.GetStringAsync("/admin/custom-designs")).Contains(saved.Number),"Admin sees custom request in management panel");
 using var outsider=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=client.BaseAddress};Check((await outsider.GetAsync($"/api/custom-designs/{saved.Id}/files/0")).StatusCode==HttpStatusCode.Unauthorized,"Anonymous private file access denied");

 var callbacks=new CustomCallbackService(factory,sms);
 await Reject(()=>callbacks.SubmitAsync("invalid",default),"Callback rejects invalid phone numbers");
 await Task.WhenAll(Enumerable.Range(0,3).Select(_=>callbacks.SubmitAsync("+98 912 000 0021",default)));
 var callback=await db.CustomCallbackRequests.Include(r=>r.SmsJobs).SingleAsync(r=>r.Phone=="09120000021");
 Check(callback.SmsJobs.Count==2&&callback.SmsJobs.All(j=>j.RequestId==null&&j.CallbackRequestId==callback.Id),"Concurrent anonymous callback requests create one request and exactly two admin jobs");
 Check(!await db.Users.AnyAsync(u=>u.Phone==callback.Phone)&&!await db.Orders.AnyAsync(),"Callback creates neither an account nor a payable order");
 for(var i=0;i<30&&await queue.ProcessOneAsync();i++){}
 Check(await db.CustomDesignSmsJobs.AnyAsync(j=>j.CallbackRequestId==callback.Id&&j.State==ShippingSmsState.Accepted),"Callback notification uses the persistent SMS queue");
 using var guest=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=client.BaseAddress};
 var contactPage=await guest.GetStringAsync("/custom-design");
 var callbackToken=WebUtility.HtmlDecode(Regex.Match(contactPage,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
 Check(callbackToken.Length>0,"Anonymous callback form has anti-forgery protection");
 Check((await guest.PostAsync("/custom-design/request-call",new FormUrlEncodedContent(new Dictionary<string,string>{{"phone","09120000022"}}))).StatusCode==HttpStatusCode.BadRequest,"Callback rejects missing anti-forgery token");
 var callbackResponse=await guest.PostAsync("/custom-design/request-call",new FormUrlEncodedContent(new Dictionary<string,string>{{"phone","09120000022"},{"__RequestVerificationToken",callbackToken}}));
 Check(callbackResponse.StatusCode==HttpStatusCode.Redirect&&callbackResponse.Headers.Location?.ToString()=="/custom-design?sent=1","Anonymous phone-only request succeeds without registration");
 Check(await db.CustomCallbackRequests.AnyAsync(r=>r.Phone=="09120000022"),"HTTP callback is stored for admin follow-up");
 var adminPage=await manager.GetStringAsync("/admin/custom-designs");
 Check(adminPage.Contains(callback.Number)&&adminPage.Contains("09120000022"),"Admin sees callback references and phone numbers");
 Check((await guest.GetStringAsync("/about")).Contains("story-page")&&(await guest.GetStringAsync("/contact")).Contains("contact-cards"),"About and contact pages render");
 Check((await guest.GetStringAsync("/sitemap.xml")).Contains("/about"),"About page is included in sitemap");
 Console.WriteLine($"{checks} custom design checks passed; no real SMS or orders sent.");
}finally{if(server is {HasExited:false}){server.Kill(true);await server.WaitForExitAsync();}await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)",connection).ExecuteNonQueryAsync();if(Path.GetDirectoryName(root)==Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)&&Path.GetFileName(root).StartsWith("papey-custom-test-"))Directory.Delete(root,true);}
sealed class Factory(DbContextOptions<AppDbContext> options):IDbContextFactory<AppDbContext>{public AppDbContext CreateDbContext()=>new(options);}
sealed class TestEnvironment:IWebHostEnvironment{public string EnvironmentName{get;set;}="Production";public string ApplicationName{get;set;}="Tests";public string ContentRootPath{get;set;}="";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public string WebRootPath{get;set;}="";public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();}
sealed class FakeSms:HttpMessageHandler{public bool FailUncertain;public List<string> Texts=[];protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));Texts.Add(doc.RootElement.TryGetProperty("messageText",out var text)?text.GetString()!:"template");return FailUncertain?new(HttpStatusCode.InternalServerError):new(HttpStatusCode.OK){Content=new StringContent("{\"status\":1,\"data\":{\"messageIds\":[123456],\"packId\":\"test\"}}")};}}
