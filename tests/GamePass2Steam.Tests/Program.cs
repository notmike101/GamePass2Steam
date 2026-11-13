using GamePass2Steam.Core;
using System.Text;

int passed = 0;
void Check(string name, Action test) { test(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool ok) { if (!ok) throw new Exception("Assertion failed"); }
void Throws(Action action) { try { action(); } catch (InvalidDataException) { return; } catch (IOException) { return; } throw new Exception("Expected safe refusal"); }
var game = new Game("Example.Game_abcd!Play", "Café Game", "Example.Game_abcd", @"C:\Games\Café", [@"C:\Games\Café\Game.exe"], "GDK", true);
const string launcher = @"C:\Apps\Game Pass\GamePass2Steam.exe";
// Independent wire fixture: object shortcuts, object 0, string appname=A, uint appid=0x80000001, nested empty tags.
byte[] fixture = [0,115,104,111,114,116,99,117,116,115,0,0,48,0,1,97,112,112,110,97,109,101,0,65,0,2,97,112,112,105,100,0,1,0,0,128,0,116,97,103,115,0,8,8,8,8];
Check("binary fixture round trips byte for byte", () => Assert(BinaryVdf.Write(BinaryVdf.Read(fixture)).SequenceEqual(fixture)));
Check("shortcut root casing is accepted and preserved", () => {
    var mixed = fixture.ToArray(); mixed[1] = (byte)'S';
    var root = BinaryVdf.Read(mixed);
    Assert(SteamShortcuts.Entries(root).Count == 1);
    Assert(BinaryVdf.Write(root).SequenceEqual(mixed));
    SteamShortcuts.Update(root, [game], launcher, false);
    Assert(root[0].Name == "Shortcuts");
    Throws(() => SteamShortcuts.Entries([root[0], root[0]]));
    Throws(() => SteamShortcuts.Entries([VdfNode.String("Shortcuts", "wrong type")]));
});
Check("reject truncated, trailing, unknown type and deep data", () => {
    Throws(() => BinaryVdf.Read(fixture[..^1]));
    Throws(() => BinaryVdf.Read([..fixture, 0]));
    Throws(() => BinaryVdf.Read([99, 0, 8]));
    Throws(() => BinaryVdf.Read(Enumerable.Repeat(new byte[] {0,65,0}, 100).SelectMany(x=>x).Concat(Enumerable.Repeat((byte)8,101)).ToArray()));
});
Check("preserve unrelated scalar types and nested metadata", () => {
    var root = BinaryVdf.Read(fixture);
    root[0].Children![0].Children!.Add(new VdfNode(7, "future", [1,2,3,4,5,6,7,8]));
    var before = BinaryVdf.Write(root[0].Children!);
    SteamShortcuts.Update(root, [game], launcher, false);
    Assert(BinaryVdf.Write([root[0].Children![0]]).SequenceEqual(before));
});
Check("repeat import updates location without duplication or erasing user fields", () => {
    var root = BinaryVdf.Read(fixture);
    SteamShortcuts.Update(root, [game], launcher, false);
    var entry = root[0].Children![1];
    var id = entry.Children!.Single(n=>n.Name=="appid").Data!.ToArray();
    entry.Children!.Add(VdfNode.String("custom", "keep"));
    SteamShortcuts.Update(root, [game], @"D:\Moved\GamePass2Steam.exe", false);
    Assert(root[0].Children!.Count==2);
    Assert(entry.Text("custom")=="keep");
    Assert(entry.Children!.Single(n=>n.Name=="appid").Data!.SequenceEqual(id));
    Assert(entry.Text("Exe")=="\"D:\\Moved\\GamePass2Steam.exe\"");
    Assert(entry.Text("LaunchOptions")=="--launch \"Example.Game_abcd!Play\"");
    SteamShortcuts.Update(root, [game], launcher, true);
    Assert(BinaryVdf.Write(root).SequenceEqual(fixture));
});
Check("reject malformed shortcut root and duplicate critical fields", () => {
    Throws(()=>SteamShortcuts.Update([VdfNode.Object("wrong")], [game], launcher, false));
    var root=BinaryVdf.Read(fixture); root[0].Children![0].Children!.Add(VdfNode.String("appname", "B"));
    Throws(()=>SteamShortcuts.Update(root,[game],launcher,false));
});
Check("reject unsafe identity and command line input", () => {
    Throws(()=>SteamShortcuts.Update(BinaryVdf.Read(fixture),[game with { Id="bad\" --evil" }],launcher,false));
    Throws(()=>SteamShortcuts.Update(BinaryVdf.Read(fixture),[game],"bad\"path",false));
});
Check("reject ambiguous ownership tags and duplicate entry indexes", () => {
    var root=BinaryVdf.Read(fixture);
    SteamShortcuts.Update(root,[game],launcher,false);
    root[0].Children![1].Children!.Single(n=>n.Name=="tags").Children!.Add(VdfNode.String("1", "GamePass2Steam:Other.Game_abcd!Play"));
    Throws(()=>SteamShortcuts.Update(root,[game],launcher,true));
    root=BinaryVdf.Read(fixture); root[0].Children!.Add(root[0].Children![0]);
    Throws(()=>SteamShortcuts.Update(root,[game],launcher,false));
});
Check("UWP lifetime requires a visible game window and ignores suspended remnants", () => {
    Assert(!SessionTracker.IsRunning("UWP", true, false));
    Assert(SessionTracker.IsRunning("UWP", true, true));
    Assert(!SessionTracker.IsRunning("UWP", false, true));
    Assert(SessionTracker.IsRunning("GDK / packaged Win32", true, false));
});
Check("atomic update backup restore and running Steam refusal", () => {
    string dir=Path.Combine(Path.GetTempPath(),"gp2s-tests-"+Guid.NewGuid()); Directory.CreateDirectory(dir);
    try {
        string path=Path.Combine(dir,"shortcuts.vdf"); File.WriteAllBytes(path,fixture);
        var store=new ShortcutStore(path,()=>false);
        string? backup=store.Apply([game],launcher,false);
        Assert(backup!=null && File.ReadAllBytes(backup).SequenceEqual(fixture));
        var after=File.ReadAllBytes(path);
        Assert(store.Apply([game],launcher,false)==null);
        Throws(()=>new ShortcutStore(path,()=>true).Apply([game],launcher,true));
        Assert(File.ReadAllBytes(path).SequenceEqual(after));
        store.Restore(backup!); Assert(File.ReadAllBytes(path).SequenceEqual(fixture));
        File.WriteAllBytes(path,[0,1]); Throws(()=>store.Apply([game],launcher,false)); Assert(File.ReadAllBytes(path).SequenceEqual(new byte[]{0,1}));
    } finally { Directory.Delete(dir,true); }
});
Check("concurrent changes detected before replacement", () => {
    string dir=Path.Combine(Path.GetTempPath(),"gp2s-tests-"+Guid.NewGuid()); Directory.CreateDirectory(dir);
    try {
        string path=Path.Combine(dir,"shortcuts.vdf"); File.WriteAllBytes(path,fixture); int calls=0;
        var store=new ShortcutStore(path,()=>{ if(++calls==2) File.WriteAllBytes(path,[8]); return false; });
        Throws(()=>store.Apply([game],launcher,false)); Assert(File.ReadAllBytes(path).SequenceEqual(new byte[]{8}));
    } finally { Directory.Delete(dir,true); }
});
Check("GDK metadata distinguishes game from bootstrap and excludes dev executables", () => {
    string xml="<Game><ExecutableList><Executable Id='Play' Name='bin/Game.exe'/><Executable Id='Dev' Name='tools.exe' IsDevOnly='true'/></ExecutableList></Game>";
    Assert(GameMetadata.Executables(xml,"Play",@"C:\Games\Café").Single().EndsWith(Path.Combine("bin","Game.exe")));
    Assert(GameMetadata.Executables(xml,"Dev",@"C:\Games\Café").Count==0);
    Throws(()=>GameMetadata.Executables("<Game><ExecutableList><Executable Id='Play' Name='../escape.exe'/></ExecutableList></Game>","Play",@"C:\Games\Café"));
    Throws(()=>GameMetadata.Executables("<!DOCTYPE Game [<!ENTITY e SYSTEM 'file:///secret'>]><Game>&e;</Game>","Play",@"C:\Games"));
});
Check("session waits for real game, follows handoff, exits and bounds failure", () => {
    var s=new SessionTracker();
    Assert(s.Observe(TimeSpan.Zero,false)==SessionState.Starting);
    Assert(s.Observe(TimeSpan.FromSeconds(50),true)==SessionState.Running);
    Assert(s.Observe(TimeSpan.FromSeconds(51),false)==SessionState.Running);
    Assert(s.Observe(TimeSpan.FromSeconds(52),true)==SessionState.Running);
    Assert(s.Observe(TimeSpan.FromSeconds(53),false)==SessionState.Running);
    Assert(s.Observe(TimeSpan.FromSeconds(57),false)==SessionState.Exited);
    Assert(new SessionTracker().Observe(TimeSpan.FromSeconds(121),false)==SessionState.Failed);
});
ArtworkChecks.Run(Check, Assert, Throws);
Check("Steam import requires per-operation authorization and leaves closed Steam closed", () => {
    int exits = 0, starts = 0, writes = 0;
    Throws(() => SteamImport.Run(false, () => true, () => exits++, () => starts++, () => writes++).GetAwaiter().GetResult());
    Assert(exits == 0 && starts == 0 && writes == 0);
    SteamImport.Run(false, () => false, () => exits++, () => starts++, () => writes++).GetAwaiter().GetResult();
    Assert(exits == 0 && starts == 0 && writes == 1);
    SteamImport.Run(true, () => false, () => exits++, () => starts++, () => writes++).GetAwaiter().GetResult();
    Assert(exits == 0 && starts == 1 && writes == 2); // Steam exited between consent and execution.
});
Check("Steam import waits for exit and restarts after success or write failure", () => {
    foreach (bool fail in new[] {false, true}) {
        bool running = true; int waits = 0, starts = 0, writes = 0;
        async Task Run() => await SteamImport.Run(true, () => running, () => {}, () => { starts++; running = true; },
            () => { Assert(!running); writes++; if (fail) throw new IOException("write failed"); },
            wait: () => { if (++waits == 3) running = false; return Task.CompletedTask; });
        if (fail) Throws(() => Run().GetAwaiter().GetResult()); else Run().GetAwaiter().GetResult();
        Assert(waits == 3 && writes == 1 && starts == 1 && running);
    }
});
Check("Steam shutdown timeout never imports or kills a running client", () => {
    int waits = 0, writes = 0, starts = 0;
    Throws(() => SteamImport.Run(true, () => true, () => {}, () => starts++, () => writes++,
        wait: () => { waits++; return Task.CompletedTask; }).GetAwaiter().GetResult());
    Assert(waits == 120 && writes == 0 && starts == 0);
});
Check("canceling a Steam import still recovers a completed shutdown without writing", () => {
    bool running = true; int starts = 0, writes = 0, waits = 0; using var cancellation = new CancellationTokenSource();
    try {
        SteamImport.Run(true, () => running, () => {}, () => starts++, () => writes++, cancellation.Token,
            () => { cancellation.Cancel(); if (++waits == 2) running = false; return Task.CompletedTask; }).GetAwaiter().GetResult();
        throw new Exception("Expected cancellation");
    } catch (OperationCanceledException) { }
    Assert(writes == 0 && starts == 1 && waits == 2);
});
Check("Steam shutdown and restart errors remain visible and prevent unsafe imports", () => {
    int writes = 0;
    Throws(() => SteamImport.Run(true, () => true, () => throw new IOException("shutdown failed"), () => {}, () => writes++).GetAwaiter().GetResult());
    Assert(writes == 0);
    bool running = true;
    try {
        SteamImport.Run(true, () => running, () => running = false, () => throw new IOException("restart failed"), () => throw new IOException("write failed")).GetAwaiter().GetResult();
        throw new Exception("Expected recovery failure");
    } catch (IOException e) { Assert(e.Message.Contains("Open Steam manually") && e.InnerException is AggregateException); }
});
Check("profile names prefer persona and keep numeric identities and duplicate-name disambiguation", () => {
    var profiles = SteamProfiles.Resolve(["1", "2", "3", "4"], """
        // Locally cached Steam accounts
        "users" {
          "76561197960265729" { "AccountName" "login-one" "PersonaName" "Café \"Player\"" }
          "76561197960265730" { "PersonaName" "Café \"Player\"" }
          "76561197960265731" { "PersonaName" " " "AccountName" "fallback" }
          "76561197960265737" { "PersonaName" "stale profile" }
        }
        """);
    Assert(profiles.Count == 4 && profiles[0].Id == "1");
    Assert(profiles[0].ToString() == "Café \"Player\" (1)" && profiles[1].ToString().EndsWith("(2)"));
    Assert(profiles[2].ToString() == "fallback (3)" && profiles[3].ToString() == "4");
});
Check("missing malformed oversized and ambiguous profile metadata falls back safely", () => {
    foreach (var text in new string?[] { null, "", "users {", "users { a }", "users { \"x", new string(' ', 1024 * 1024 + 1),
        "users { 76561197960265729 { PersonaName one PersonaName two } }", "users {} users {}" })
        Assert(SteamProfiles.Resolve(["1"], text).Single().ToString() == "1");
    Assert(SteamProfiles.Resolve(["1"], "users { 76561197960265729 { PersonaName one } 76561197960265729 { PersonaName two } }").Single().Name == null);
    Assert(SteamProfiles.Resolve(["1"], "users { 1 { PersonaName wrong } }").Single().Name == null);
});
Check("profile loading handles absent locked and invalid UTF8 metadata without losing folders", () => {
    string dir = Path.Combine(Path.GetTempPath(), "gp2s-profiles-" + Guid.NewGuid());
    try {
        Directory.CreateDirectory(Path.Combine(dir,"userdata","1")); Directory.CreateDirectory(Path.Combine(dir,"userdata","not-an-id"));
        Directory.CreateDirectory(Path.Combine(dir,"config")); string path = Path.Combine(dir,"config","loginusers.vdf");
        Assert(SteamProfiles.Load(dir).Single().Id == "1");
        File.WriteAllText(path,"users { 76561197960265729 { PersonaName Player } }");
        Assert(SteamProfiles.Load(dir).Single().Name == "Player");
        using (var locked = new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            Assert(SteamProfiles.Load(dir).Single().ToString() == "1");
        File.WriteAllBytes(path,[0xff,0xff,0xff]); Assert(SteamProfiles.Load(dir).Single().ToString() == "1");
    } finally { Directory.Delete(dir,true); }
});
Console.WriteLine($"{passed} checks passed");
if (args is ["--compare", var beforePath, var afterPath, var expectedId])
{
    var before = SteamShortcuts.Entries(BinaryVdf.Read(File.ReadAllBytes(beforePath)));
    var after = SteamShortcuts.Entries(BinaryVdf.Read(File.ReadAllBytes(afterPath)));
    Assert(after.Count == before.Count + 1);
    Assert(after.Count(e => SteamShortcuts.ManagedId(e) == expectedId) == 1);
    Assert(BinaryVdf.Write(before).SequenceEqual(BinaryVdf.Write(after.Where(e => SteamShortcuts.ManagedId(e) != expectedId).ToList())));
    Console.WriteLine($"INTEGRATION: preserved {before.Count} existing entries byte-for-byte; exactly one new managed entry.");
}
