using System.Net;
using System.Text;
using GamePass2Steam.Core;

internal static class ArtworkChecks
{
    private const string Key = "test-key-not-a-real-secret";
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Image() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) { Headers = { ContentType = new("image/png") } } };
    private static HttpResponseMessage Asset() => Json("""{"success":true,"data":[{"id":10,"score":4,"url":"https://cdn2.steamgriddb.com/grid/test.png","author":{"name":"Test Artist"}}]}""");

    public static void Run(Action<string, Action> check, Action<bool> assert, Action<Action> throws)
    {
        check("Steam restart waits for asynchronous artwork completion and recovers async failure", () => {
            foreach (bool fail in new[] {false,true}) {
                bool running = true; List<string> events = [];
                var operation = SteamImport.RunAsync(true,()=>running,()=>{running=false;events.Add("exit");},()=>{running=true;events.Add("restart");},async()=>{
                    assert(!running); events.Add("shortcuts"); await Task.Yield(); assert(!running); events.Add("artwork"); if (fail) throw new IOException("artwork failed");
                });
                if (fail) throws(()=>operation.GetAwaiter().GetResult()); else operation.GetAwaiter().GetResult();
                assert(events.SequenceEqual(new[]{"exit","shortcuts","artwork","restart"}) && running);
            }
        });
        check("artwork matching ignores punctuation but never guesses editions or ambiguous titles", () => {
            var game = new ArtworkGame(1,"Café Game: Two");
            assert(SteamGridDb.ExactMatch("CAFÉ Game™ - Two", [game]) == game);
            assert(SteamGridDb.ExactMatch("Café Game Two Deluxe Edition", [game]) == null);
            assert(SteamGridDb.ExactMatch(game.Name, [game,new(2,game.Name)]) == null);
            assert(SteamGridDb.ExactMatch("Unknown", [game]) == null);
        });
        check("SteamGridDB sends authorization only to API and filters static safe PNG artwork", () => {
            int apiCalls = 0, imageCalls = 0;
            using var http = new HttpClient(new Handler(request => {
                if (request.RequestUri!.Host == "www.steamgriddb.com") {
                    apiCalls++; assert(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == Key);
                    if (request.RequestUri.AbsolutePath.Contains("search")) return Json("""{"success":true,"data":[{"id":123,"name":"Game"}]}""");
                    assert(request.RequestUri.Query.Contains("types=static") && request.RequestUri.Query.Contains("nsfw=false") && request.RequestUri.Query.Contains("image/png"));
                    return Asset();
                }
                imageCalls++; assert(request.Headers.Authorization == null); return Image();
            }));
            var api = new SteamGridDb(http,Key);
            assert(api.Search("Game",default).GetAwaiter().GetResult().Single().Id == 123);
            var asset = api.Asset(123,ArtworkKind.Portrait,default).GetAwaiter().GetResult()!;
            assert(api.Download(asset,default).GetAwaiter().GetResult().SequenceEqual(Png));
            assert(apiCalls == 2 && imageCalls == 1);
            throws(() => api.Download(asset with {Url = new Uri("https://example.com/image.png")},default).GetAwaiter().GetResult());
            assert(imageCalls == 1);
        });
        check("artwork accepts mixed-case PNG media types without bypassing content validation", () => {
            foreach (bool valid in new[] {true, false}) {
                using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) {
                    Content = new ByteArrayContent(valid ? Png : [1, 2, 3]) { Headers = { ContentType = new("Image/PNG") } }
                }));
                var api = new SteamGridDb(http, Key);
                var asset = new ArtworkAsset(1, new("https://cdn2.steamgriddb.com/x"), "", 0);
                if (valid) assert(api.Download(asset, default).GetAwaiter().GetResult().SequenceEqual(Png));
                else throws(() => api.Download(asset, default).GetAwaiter().GetResult());
            }
        });
        check("artwork rejects untrusted URLs invalid PNG content and oversized responses", () => {
            foreach (string url in new[] {"http://cdn2.steamgriddb.com/x", "https://cdn2.steamgriddb.com.evil.test/x", "https://127.0.0.1/x", "https://user:pass@cdn2.steamgriddb.com/x", "https://cdn2.steamgriddb.com:8443/x"})
                assert(!SteamGridDb.AllowedImageUri(new(url)));
            throws(() => SteamGridDb.ValidatePng([])); throws(() => SteamGridDb.ValidatePng(Png[..^1]));
            var animated = Png.ToArray(); Encoding.ASCII.GetBytes("acTL").CopyTo(animated,37); throws(() => SteamGridDb.ValidatePng(animated));
            var huge = Png.ToArray(); huge[16] = 127; throws(() => SteamGridDb.ValidatePng(huge));
            foreach (bool oversized in new[] {false,true}) {
                using var http = new HttpClient(new Handler(_ => {
                    var response = Image();
                    if (oversized) response.Content.Headers.ContentLength = 21 * 1024 * 1024;
                    else response.Content.Headers.ContentType = new("text/html");
                    return response;
                }));
                var api = new SteamGridDb(http,Key);
                throws(() => api.Download(new(1,new("https://cdn2.steamgriddb.com/x"),"",0),default).GetAwaiter().GetResult());
            }
        });
        check("non-JSON artwork responses have safe messages without response content", () => {
            foreach (string body in new[] {"<html>" + Key + "</html>", "", "{", "null", "[]"}) {
                using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") }));
                try { new SteamGridDb(http, Key).Search("Game", default).GetAwaiter().GetResult(); throw new Exception("Expected invalid response failure"); }
                catch (ArtworkUnavailableException e) { assert(!e.ToString().Contains(Key) && !e.Stop); }
            }
        });
        check("artwork authentication rate limit and service failures have safe messages", () => {
            foreach (var status in new[] {HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden,HttpStatusCode.TooManyRequests,HttpStatusCode.ServiceUnavailable}) {
                using var http = new HttpClient(new Handler(_ => new(status) {Content = new StringContent(Key)}));
                try { new SteamGridDb(http,Key).Search("Game",default).GetAwaiter().GetResult(); throw new Exception("Expected service failure"); }
                catch (ArtworkUnavailableException e) { assert(!e.ToString().Contains(Key)); assert(e.Stop == (status != HttpStatusCode.ServiceUnavailable)); }
            }
        });
        check("artwork uses stored AppID and selected profile and preserves custom images and icon metadata", () => {
            string directory = Path.Combine(Path.GetTempPath(),"gp2s-art-"+Guid.NewGuid());
            try {
                string config = Path.Combine(directory,"userdata","42","config"), other = Path.Combine(directory,"userdata","99","config","grid");
                Directory.CreateDirectory(config); Directory.CreateDirectory(other);
                var game = new Game("Example.Game_abcd!Play","Game","Example.Game_abcd","",[],"",true);
                var root = new List<VdfNode> {VdfNode.Object("shortcuts")}; SteamShortcuts.Update(root,[game],Path.Combine(directory,"app.exe"),false);
                var entry = SteamShortcuts.Entries(root).Single();
                int idIndex = entry.Children!.FindIndex(n => n.Name == "appid"); entry.Children[idIndex] = VdfNode.Integer("appid",3456789012);
                int iconIndex = entry.Children.FindIndex(n => n.Name == "icon"); entry.Children[iconIndex] = VdfNode.String("icon","custom.ico");
                string vdf = Path.Combine(config,"shortcuts.vdf"); File.WriteAllBytes(vdf,BinaryVdf.Write(root)); byte[] before = File.ReadAllBytes(vdf);
                string grid = Path.Combine(config,"grid"); Directory.CreateDirectory(grid); byte[] custom = [1,2,3];
                File.WriteAllBytes(Path.Combine(grid,"3456789012p.jpg"),custom);
                int requests = 0;
                using var http = new HttpClient(new Handler(r => {requests++; return r.RequestUri!.Host == "www.steamgriddb.com" ? Asset() : Image();}));
                var installer = new ArtworkInstaller(new(http,Key),()=>false,SteamGridDb.ValidatePng); var store = new ShortcutStore(vdf,()=>false);
                var result = installer.Fill(store,game.Id,new(123,"Game"),default).GetAwaiter().GetResult();
                assert(result.Installed == 3 && result.Warnings.Count == 0 && requests == 6);
                foreach (string file in new[] {"3456789012.png","3456789012_hero.png","3456789012_logo.png"}) assert(File.ReadAllBytes(Path.Combine(grid,file)).SequenceEqual(Png));
                assert(File.ReadAllBytes(Path.Combine(grid,"3456789012p.jpg")).SequenceEqual(custom));
                assert(File.ReadAllBytes(vdf).SequenceEqual(before)); assert(!Directory.EnumerateFiles(other).Any());
                assert(ArtworkInstaller.RememberedMatch(store,game.Id.ToUpperInvariant())?.Id == 123);
                result = installer.Fill(store,game.Id,new(123,"Game"),default).GetAwaiter().GetResult();
                assert(result.Installed == 0 && requests == 6 && !ArtworkInstaller.Missing(store,game.Id));
                assert(File.ReadAllText(Path.Combine(grid,"3456789012_logo.png.gp2s-source.json")).Contains("Test Artist"));
            } finally {Directory.Delete(directory,true);}
        });
        check("missing icons are linked through guarded shortcut backups and retry without downloading", () => {
            string directory = Path.Combine(Path.GetTempPath(),"gp2s-icon-"+Guid.NewGuid()); Directory.CreateDirectory(directory);
            try {
                var game = new Game("Example.Game_abcd!Play","Game","Example.Game_abcd","",[],"",true);
                var store = new ShortcutStore(Path.Combine(directory,"shortcuts.vdf"),()=>false); store.Apply([game],Path.Combine(directory,"app.exe"),false);
                int requests = 0;
                using var http = new HttpClient(new Handler(r=>{requests++; return r.RequestUri!.Host == "www.steamgriddb.com" ? Asset() : Image();}));
                var installer = new ArtworkInstaller(new(http,Key),()=>false,SteamGridDb.ValidatePng);
                assert(installer.Fill(store,game.Id.ToLowerInvariant(),new(123,"Game"),default).GetAwaiter().GetResult().Installed == 5);
                var entry = SteamShortcuts.Entries(store.Read()).Single(); string icon = entry.Text("icon")!;
                assert(File.Exists(icon) && Path.GetFileName(icon).EndsWith("_icon.png") && Directory.GetFiles(directory,"*.bak").Length == 1);
                assert(!ArtworkInstaller.Missing(store,game.Id));
                installer.Fill(store,game.Id,new(123,"Game"),default).GetAwaiter().GetResult(); assert(requests == 10);
            } finally {Directory.Delete(directory,true);}
        });
        check("failed icon attribution stays missing and completes on a later import", () => {
            string directory = Path.Combine(Path.GetTempPath(),"gp2s-attribution-"+Guid.NewGuid()); Directory.CreateDirectory(directory);
            try {
                var game = new Game("Example.Game_abcd!Play","Game","Example.Game_abcd","",[],"",true);
                string vdf = Path.Combine(directory,"shortcuts.vdf");
                var store = new ShortcutStore(vdf,()=>false); store.Apply([game],Path.Combine(directory,"app.exe"),false);
                byte[] before = File.ReadAllBytes(vdf);
                var entry = SteamShortcuts.Entries(store.Read()).Single();
                uint appId = BitConverter.ToUInt32(entry.Children!.Single(n=>n.Name=="appid").Data!);
                string grid = Path.Combine(directory,"grid"), icon = Path.Combine(grid,ArtworkInstaller.Stem(appId,ArtworkKind.Icon)+".png");
                string attribution = icon+".gp2s-source.json";
                Directory.CreateDirectory(attribution); // A directory at the file path forces a real attribution write failure.
                int requests = 0;
                using var http = new HttpClient(new Handler(r=>{requests++; return r.RequestUri!.Host == "www.steamgriddb.com" ? Asset() : Image();}));
                var installer = new ArtworkInstaller(new(http,Key),()=>false,SteamGridDb.ValidatePng);
                var result = installer.Fill(store,game.Id,new(123,"Game"),default).GetAwaiter().GetResult();
                assert(result.Warnings.Count == 1 && result.Installed == 4 && requests == 10);
                assert(!File.Exists(icon) && ArtworkInstaller.Missing(store,game.Id));
                assert(File.ReadAllBytes(vdf).SequenceEqual(before) && !Directory.GetFiles(grid,"*.tmp").Any());
                var completed = Directory.GetFiles(grid,"*.png").ToDictionary(p=>p,File.ReadAllBytes);
                Directory.Delete(attribution);
                result = installer.Fill(store,game.Id,new(123,"Game"),default).GetAwaiter().GetResult();
                assert(result.Installed == 1 && result.Warnings.Count == 0 && requests == 12);
                assert(File.ReadAllBytes(icon).SequenceEqual(Png) && File.ReadAllText(attribution).Contains("Test Artist"));
                assert(SteamShortcuts.Entries(store.Read()).Single().Text("icon") == icon && !ArtworkInstaller.Missing(store,game.Id));
                foreach (var pair in completed) assert(File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value));
                assert(installer.Fill(store,game.Id,new(123,"Game"),default).GetAwaiter().GetResult().Installed == 0 && requests == 12);
            } finally {Directory.Delete(directory,true);}
        });
        check("artwork failures and Steam starting never roll back imported shortcuts or overwrite art", () => {
            foreach (bool steamStarts in new[] {false,true}) {
                string directory = Path.Combine(Path.GetTempPath(),"gp2s-artfail-"+Guid.NewGuid()); Directory.CreateDirectory(directory);
                try {
                    var game = new Game("Example.Game_abcd!Play","Game","Example.Game_abcd","",[],"",true); bool running = false;
                    var store = new ShortcutStore(Path.Combine(directory,"shortcuts.vdf"),()=>running); store.Apply([game],Path.Combine(directory,"app.exe"),false);
                    byte[] before = File.ReadAllBytes(Path.Combine(directory,"shortcuts.vdf"));
                    using var http = new HttpClient(new Handler(r=> {
                        if (r.RequestUri!.Host == "www.steamgriddb.com") return Asset();
                        if (steamStarts) { running = true; return Image(); }
                        return new(HttpStatusCode.ServiceUnavailable);
                    }));
                    var installer = new ArtworkInstaller(new(http,Key),()=>running,SteamGridDb.ValidatePng);
                    if (steamStarts) throws(()=>installer.Fill(store,game.Id,new(123,"Game"),default).GetAwaiter().GetResult());
                    else assert(installer.Fill(store,game.Id,new(123,"Game"),default).GetAwaiter().GetResult().Warnings.Count == 5);
                    assert(File.ReadAllBytes(Path.Combine(directory,"shortcuts.vdf")).SequenceEqual(before));
                    assert(!Directory.GetFiles(Path.Combine(directory,"grid"),"*.png").Any());
                } finally {Directory.Delete(directory,true);}
            }
        });
    }
}
