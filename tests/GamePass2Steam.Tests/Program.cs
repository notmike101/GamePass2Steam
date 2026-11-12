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
Console.WriteLine($"{passed} checks passed");
