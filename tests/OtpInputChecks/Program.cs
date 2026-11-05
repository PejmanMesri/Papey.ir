using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Papey.Components.Shared;

var count = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; Console.WriteLine("PASS: " + message); }
var services = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime>(new NoBrowser()).BuildServiceProvider();
await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<OtpCodeInput>()).ToHtmlString());
Check(Regex.Matches(html, "<input\\b").Count == 5 && Regex.Matches(html, "inputmode=\"numeric\"").Count == 5, "Rendered OTP entry has exactly five numeric-keyboard inputs");
Check(html.Contains("dir=\"ltr\"") && html.Contains("autocomplete=\"one-time-code\""), "Digit order and mobile SMS autofill are declared in the rendered component");
var disabled = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<OtpCodeInput>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Disabled"] = true }))).ToHtmlString());
Check(Regex.Matches(disabled, "<input[^>]+\\sdisabled(?:[ >])").Count == 5, "Verification busy state disables all five boxes");
var received = ""; var submissions = 0; var receiver = new object();
var input = new OtpCodeInput {
    ValueChanged = EventCallback.Factory.Create<string>(receiver, (string value) => received = value),
    Completed = EventCallback.Factory.Create(receiver, () => { submissions++; })
};
await input.Update("۱۲۳۴", true);
Check(received == "1234" && submissions == 0, "Partial codes cannot trigger automatic verification even if a client claims completion");
await input.Update("٠١٢٣٤", true);
Check(received == "01234" && submissions == 1, "A complete normalized code reaches the parent before one automatic submit");
await input.Update("123456", true); await input.Update("abcde", true);
Check(received == "01234" && submissions == 1, "Malformed and oversized interop values are ignored");
input.Disabled = true; await input.Update("54321", true);
Check(received == "01234" && submissions == 1, "A busy verification cannot accept another completion callback");
await input.DisposeAsync();
Console.WriteLine($"{count} rendered OTP and completion checks passed; no SMS or account requests.");
sealed class NoBrowser : IJSRuntime {
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new Exception("No browser calls during static rendering");
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
}
