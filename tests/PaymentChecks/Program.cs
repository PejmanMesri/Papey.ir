using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Papey.Services;

int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS: "+name);}
const string merchant="12dcf31b-48c6-427c-bb90-36440eeae702";
var authority="A"+new string('0',28)+"wwOGYpd";
Dictionary<string,string?> Settings(bool sandbox=false)=>new(){["ZARINPAL_MERCHANT_ID"]=merchant,["ZARINPAL_SANDBOX"]=sandbox.ToString(),["ZARINPAL_CALLBACK_URL"]="https://papey.ir/payment/callback"};
ZarinpalService Service(FakeHttp handler,Dictionary<string,string?>? settings=null)=>new(new ConfigurationBuilder().AddInMemoryCollection(settings??Settings()).Build(),NullLogger<ZarinpalService>.Instance,new HttpClient(handler));
var fake=new FakeHttp();var service=Service(fake);
Check(service.IsConfigured&&!service.IsSandbox&&service.GatewayBase=="https://payment.zarinpal.com","Production configuration uses official live endpoint");
fake.Json=JsonSerializer.Serialize(new{data=new{code=100,authority},errors=Array.Empty<string>()});var request=await service.RequestPaymentAsync(1250000,"Order PY-TEST");Check(request.Success&&request.Authority==authority&&request.PaymentUrl=="https://payment.zarinpal.com/pg/StartPay/"+authority,"Successful production request yields safe StartPay URL");
using(var doc=JsonDocument.Parse(fake.Body!)){Check(doc.RootElement.GetProperty("amount").GetInt64()==1250000&&doc.RootElement.GetProperty("currency").GetString()=="IRR","Request sends integer rial amount and explicit currency");Check(doc.RootElement.GetProperty("callback_url").GetString()=="https://papey.ir/payment/callback","Callback uses the production HTTPS domain");}
Check(fake.Url=="https://payment.zarinpal.com/pg/v4/payment/request.json","Request calls the official v4 endpoint");
Check(ZarinpalService.ToRials(125000m)==1250000&&ZarinpalService.ToRials(.1m)==1,"Toman-to-rial conversion is exact");
foreach(var invalid in new[]{0m,-1m,.01m,decimal.MaxValue}){bool rejected=false;try{ZarinpalService.ToRials(invalid);}catch(Exception ex)when(ex is ArgumentOutOfRangeException or OverflowException){rejected=true;}Check(rejected,"Invalid or overflowing payment amount rejected");}
foreach(var code in new[]{100,101}){fake.Json=$$"""{"data":{"code":{{code}},"ref_id":123456789},"errors":[]}""";var verify=await service.VerifyPaymentAsync(authority,1250000);Check(verify.Success&&verify.RefId==123456789,$"Verification code {code} preserves payment tracking reference");Check(fake.Url=="https://payment.zarinpal.com/pg/v4/payment/verify.json","Verification uses official live endpoint");}
fake.Json="{\"data\":[],\"errors\":{\"code\":-9,\"message\":\"private provider details\"}}";var failure=await service.RequestPaymentAsync(1000,"Test");Check(!failure.Success&&failure.Error!.Contains("-9")&&!failure.Error.Contains("private"),"Provider error object is parsed without exposing raw details");
Check(!(await service.VerifyPaymentAsync(authority,1000)).Success,"Provider verification rejection cannot confirm a sale");
foreach(var body in new[]{"{}","{\"data\":[],\"errors\":[]}","<html>Unavailable</html>","{\"data\":{\"code\":100,\"ref_id\":0},\"errors\":[]}"}){fake.Json=body;Check(!(await service.VerifyPaymentAsync(authority,1000)).Success,"Malformed or zero-reference result cannot confirm payment");}
fake.Json="{\"data\":{\"code\":100,\"authority\":\"https://malicious.example/\"},\"errors\":[]}";Check(!(await service.RequestPaymentAsync(1000,"Test")).Success,"Gateway cannot inject a redirect URL as an authority");
fake.Json=$$"""{"data":{"code":100,"authority":"{{authority}}"},"errors":[]}""";fake.Status=HttpStatusCode.InternalServerError;Check(!(await service.RequestPaymentAsync(1000,"Test")).Success,"HTTP failure cannot masquerade as successful request");fake.Status=HttpStatusCode.OK;
var calls=fake.Calls;Check(!(await service.VerifyPaymentAsync("",1000)).Success&&!(await service.VerifyPaymentAsync("../../anything",1000)).Success&&fake.Calls==calls,"Invalid callback authorities never contact provider");
Check(!(await service.RequestPaymentAsync(0,"Test")).Success&&fake.Calls==calls,"Nonpositive request never contacts provider");
var empty=Settings();empty["ZARINPAL_MERCHANT_ID"]=Guid.Empty.ToString();Check(!Service(fake,empty).IsConfigured&&!(await Service(fake,empty).RequestPaymentAsync(1000,"Test")).Success,"Missing production merchant fails closed");
var unsafeCallback=Settings();unsafeCallback["ZARINPAL_CALLBACK_URL"]="http://papey.ir/payment/callback";Check(!Service(fake,unsafeCallback).IsConfigured,"Production callback requires HTTPS");
var sandbox=Service(fake,Settings(true));Check(sandbox.IsSandbox&&sandbox.GatewayBase=="https://sandbox.zarinpal.com","Sandbox remains isolated from production");
fake.Timeout=true;Check(!(await service.RequestPaymentAsync(1000,"Test")).Success&&!(await service.VerifyPaymentAsync(authority,1000)).Success,"Network timeouts cannot confirm payment or leak exception details");
var a=new DefaultHttpContext();CartIdentity.Ensure(a,true);var b=new DefaultHttpContext();CartIdentity.Ensure(b,true);Check((string)a.Items[CartIdentity.ItemKey]!!=(string)b.Items[CartIdentity.ItemKey]!,"Separate visitors receive distinct private basket identifiers");Check(a.Response.Headers.SetCookie.ToString().Contains("httponly")&&a.Response.Headers.SetCookie.ToString().Contains("secure"),"Basket identity cookie is HttpOnly and Secure in production");
Check(!CartIdentity.Valid("papey:cart")&&!CartIdentity.Valid(Guid.Empty.ToString("N")),"Invalid and empty basket identities rejected");
Console.WriteLine($"{checks} payment configuration and protocol checks passed; no real provider requests.");
sealed class FakeHttp:HttpMessageHandler{public string Json="{}";public string? Body,Url;public bool Timeout;public int Calls;public HttpStatusCode Status=HttpStatusCode.OK;protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;Body=await request.Content!.ReadAsStringAsync(ct);Url=request.RequestUri!.ToString();if(Timeout)throw new TaskCanceledException();return new(Status){Content=new StringContent(Json)};}}
