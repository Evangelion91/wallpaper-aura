using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;
using HidSharp;

if (args.Contains("--self-test")) { Telemetry.Test(); Layout.Test(); return 0; }
if (args.Contains("--check-layout")) {
 string? Value(string name) => args.FirstOrDefault(a=>a.StartsWith(name+"=",StringComparison.Ordinal))?[(name.Length+1)..];
 if (!int.TryParse(Value("--ram-gib"),out int ram) || !int.TryParse(Value("--gpu-max-watts"),out int gpu)) { Console.Error.WriteLine("Specify --ram-gib and --gpu-max-watts"); return 2; }
 try { var packets=Layout.Prepare(1024,ram,gpu); Console.WriteLine($"Compatible layout: {packets.Count} widgets. No device writes performed."); return 0; }
 catch(Exception ex) { Console.Error.WriteLine("Unsupported layout: " + ex.Message); return 2; }
}

if (args.Contains("--stop")) {
    try { using var stop = EventWaitHandle.OpenExisting("Local\\NexLcd.Stop"); stop.Set(); }
    catch (WaitHandleCannotBeOpenedException) { }
    return 0;
}
// One LCD owner: telemetry and optional image/layout updates. No firmware or fan control.
using var mutex = new Mutex(true, "Local\\NexLcd.Telemetry", out bool first);
if (!first) return 1;
using var cancel = new CancellationTokenSource();
using var stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\NexLcd.Stop");
var stopWait = ThreadPool.RegisterWaitForSingleObject(stopEvent, (_, _) => cancel.Cancel(), null, -1, true);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
bool send = args.Contains("--send"), list = args.Contains("--list");
using var log = args.FirstOrDefault(a => a.StartsWith("--log=")) is string logArg ? new RollingLog(logArg[6..]) : null;
if (log != null) { var output = TextWriter.Synchronized(log); Console.SetOut(output); Console.SetError(output); }
int duration = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--seconds="))?.Split('=')[1], out var n) ? n : 0;
if (duration > 0) cancel.CancelAfter(TimeSpan.FromSeconds(duration));
using var reader = new Afterburner();
HidStream? stream = null;
HidDevice? lcdDevice = null;
byte[]? report = null;
int ticks = 0;
string? Arg(string name) => args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length+1)..];
bool background=args.Contains("--background");
int ramGiB=0, gpuMaxW=0;
if (background && (!send || !int.TryParse(Arg("--ram-gib"),out ramGiB) || ramGiB < 1 || ramGiB > 255 || !int.TryParse(Arg("--gpu-max-watts"),out gpuMaxW) || gpuMaxW < 1 || gpuMaxW > 65535)) {
 Console.Error.WriteLine("--background requires --send, --ram-gib=1..255 and --gpu-max-watts=1..65535"); return 2;
}
if (background) {
 try { Layout.Prepare(1024,ramGiB,gpuMaxW); }
 catch(Exception ex) { Console.Error.WriteLine("Unsupported LCD layout; no writes performed: " + ex.Message); return 2; }
}
string bgFile = Path.Combine(AppContext.BaseDirectory,"wallpaper-current.jpg");
DateTime applied = DateTime.MinValue;
Process? bgWorker = null;
if (background) {
 try {
  if (File.Exists(bgFile)) File.Delete(bgFile);
  var psi = new ProcessStartInfo(Arg("--python") ?? "python.exe") { UseShellExecute=false, CreateNoWindow=true, WorkingDirectory=AppContext.BaseDirectory };
  psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"lcd_wallpaper.py"));
  bgWorker=Process.Start(psi);
 } catch(Exception ex) { Console.Error.WriteLine("Wallpaper worker start failed: " + ex.Message); }
}
try {
    do {
        try {
            var sensors = reader.Read();
            if (list) {
                foreach (var s in sensors) Console.WriteLine($"{s.Id:X4} GPU={s.Gpu} {s.Name}: {s.Value} {s.Unit}");
                foreach (var d in DeviceList.Local.GetHidDevices(0x1f3a, 0x6502)) Console.WriteLine($"LCD {d.DevicePath} output={d.GetMaxOutputReportLength()}");
                return 0;
            }
            var packet = Telemetry.Build(sensors);
            if (send) {
                var vendors = Process.GetProcessesByName("PhanteksNexLinq");
                bool running = vendors.Length > 0;
                foreach (var p in vendors) p.Dispose();
                if (running) throw new InvalidOperationException("Exit Phanteks NexLinq before sending LCD telemetry.");
                if (stream == null) {
                    var device = DeviceList.Local.GetHidDevices(0x1f3a, 0x6502).Single(d => d.DevicePath.Contains("mi_01", StringComparison.OrdinalIgnoreCase));
                    int size = device.GetMaxOutputReportLength();
                    if (size < packet.Length || size > 65536) throw new InvalidDataException($"Unexpected HID report size {size}.");
                    lcdDevice=device; stream = device.Open(); stream.WriteTimeout = 1000; report = new byte[size];
                }
                Array.Clear(report!); packet.CopyTo(report!, 0); stream.Write(report!);
                if (background && File.Exists(bgFile)) {
                    var modified=File.GetLastWriteTimeUtc(bgFile);
                    if (modified != applied) {
                        var layout=Layout.Prepare(lcdDevice!.GetMaxOutputReportLength(),ramGiB,gpuMaxW);
                        ImageUpload.SendExisting(stream,lcdDevice!,bgFile,layout);
                        applied=modified; Console.WriteLine($"{DateTime.Now:O} wallpaper applied");
                    }
                }
            }
            if (ticks++ % 10 == 0) {
                using var self = Process.GetCurrentProcess();
                Console.WriteLine($"{DateTime.Now:O} {(send ? "sent" : "dry-run")} CPU={packet[21]}C GPU={packet[22]}C handles={self.HandleCount} privateMiB={self.PrivateMemorySize64 / 1048576}");
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            stream?.Dispose(); stream = null; reader.Reset();
            Console.Error.WriteLine($"{DateTime.Now:O} {ex.GetType().Name}: {ex.Message}");
            if (list) return 2;
        }
        await Task.Delay(1000, cancel.Token);
    } while (!cancel.IsCancellationRequested);
} catch (OperationCanceledException) { }
finally { stream?.Dispose(); if (bgWorker != null) { try { if (!bgWorker.HasExited) bgWorker.Kill(); } catch {} bgWorker.Dispose(); } stopWait.Unregister(null); }
return 0;

internal record Sensor(uint Id, uint Gpu, string Name, string Unit, float Value);
internal sealed class RollingLog : TextWriter {
    readonly string path;
    StreamWriter writer;
    public override Encoding Encoding => Encoding.UTF8;
    public RollingLog(string path) {
        this.path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
        writer = Open();
    }
    StreamWriter Open() => new(path, true, Encoding.UTF8) { AutoFlush = true };
    public override void WriteLine(string? value) {
        if (writer.BaseStream.Length > 1048576) {
            writer.Dispose(); File.Move(path, path + ".1", true); writer = Open();
        }
        writer.WriteLine(value);
    }
    protected override void Dispose(bool disposing) { if (disposing) writer.Dispose(); base.Dispose(disposing); }
}
internal sealed class Afterburner : IDisposable {
    MemoryMappedFile? map;
    MemoryMappedViewAccessor? view;
    public List<Sensor> Read() {
        if (view == null) {
            map = MemoryMappedFile.OpenExisting("MAHMSharedMemory", MemoryMappedFileRights.Read);
            view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        }
        var v = view;
        if (v.ReadUInt32(0) != 0x4D41484D || v.ReadUInt32(4) < 0x20000) throw new InvalidDataException("Afterburner monitoring not initialized.");
        uint header = v.ReadUInt32(8), count = v.ReadUInt32(12), stride = v.ReadUInt32(16);
        if (header < 32 || count > 4096 || stride < 1324 || (long)header + (long)count * stride > v.Capacity) throw new InvalidDataException("Invalid MAHM layout.");
        long age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - v.ReadUInt32(20);
        if (age > 10 || age < -10) throw new InvalidDataException("Afterburner readings stale; LCD updates paused.");
        var result = new List<Sensor>((int)count);
        byte[] text = new byte[260];
        for (int i = 0; i < count; i++) {
            long offset = header + (long)i * stride;
            string ReadText(long at) { v.ReadArray(at, text, 0, text.Length); int end = Array.IndexOf(text, (byte)0); return Encoding.Latin1.GetString(text, 0, end < 0 ? text.Length : end); }
            string name = ReadText(offset), unit = ReadText(offset + 260);
            float value = v.ReadSingle(offset + 1300);
            if (float.IsFinite(value) && value != float.MaxValue) result.Add(new(v.ReadUInt32(offset + 1320), v.ReadUInt32(offset + 1316), name, unit, value));
        }
        return result;
    }
    public void Reset() { view?.Dispose(); view = null; map?.Dispose(); map = null; }
    public void Dispose() => Reset();
}
internal static class Telemetry {
    public static void Test() {
        var s = new List<Sensor> {
            new(0x80, uint.MaxValue, "CPU temperature", "C", 67),
            new(0, 0, "GPU temperature", "C", 55),
            new(0x91, 0, "RAM usage", "MB", 32.45f * 1024),
            new(0x61, 0, "Power", "W", 601),
            new(0x20, 0, "Core clock", "MHz", 2655),
            new(0x31, 0, "Memory usage", "MB", 22901)
        };
        var b = Build(s);
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAILED: " + name); }
        Check(b.Length == 134 && b[0] == 1 && b[1] == 0x21 && b[10] == 123, "command framing");
        Check(b[21] == 67 && b[22] == 55, "temperature offsets");
        Check(BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(75)) == 601, "GPU watts offset and endianness");
        Check(BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(73)) == 2655, "GPU clock offset");
        Check(b[93] == 32 && b[94] == 45, "RAM fixed point GiB");
        s.RemoveAt(0);
        bool rejected = false;
        try { Build(s); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "missing CPU must not become fake zero temperature");
        Console.WriteLine("PASS: packet framing, field offsets, endianness, RAM conversion, missing sensor guard");
    }
    public static byte[] Build(List<Sensor> sensors) {
        byte[] report = new byte[134]; report[0] = 1; report[1] = 0x21; report[10] = 123;
        var p = report.AsSpan(11);
        BinaryPrimitives.WriteUInt32BigEndian(p, checked((uint)DateTimeOffset.Now.ToUnixTimeSeconds()));
        float? Get(uint id, uint? gpu = null) => sensors.FirstOrDefault(s => s.Id == id && (!gpu.HasValue || s.Gpu == gpu))?.Value;
        float? cpu = sensors.FirstOrDefault(s => s.Id == 0x80 && s.Name.Equals("CPU temperature", StringComparison.OrdinalIgnoreCase))?.Value;
        cpu ??= sensors.Where(s => s.Id == 0x80).Select(s => (float?)s.Value).DefaultIfEmpty(null).Max();
        if (!cpu.HasValue || !Get(0, 0).HasValue || !Get(0x91).HasValue || !Get(0x61, 0).HasValue)
            throw new InvalidDataException("Enable CPU temperature, GPU temperature, RAM usage and GPU power in Afterburner.");
        static byte Byte(float? v) => (byte)Math.Clamp(Math.Round(v ?? 0), 0, 255);
        static void Word(Span<byte> b, int at, float? v) => BinaryPrimitives.WriteUInt16BigEndian(b[at..], (ushort)Math.Clamp(Math.Round(v ?? 0), 0, 65535));
        p[10] = Byte(cpu); p[56] = Byte(Get(0x90)); Word(p, 57, Get(0xa0)); Word(p, 59, Get(0x100));
        p[11] = Byte(Get(0, 0)); p[61] = Byte(Get(0x30, 0)); Word(p, 62, Get(0x20, 0)); Word(p, 64, Get(0x61, 0)); Word(p, 66, Get(0x31, 0));
        double ram = Math.Round(Get(0x91)!.Value / 1024.0, 2);
        p[82] = (byte)Math.Clamp((int)ram, 0, 255); p[83] = (byte)Math.Clamp((int)Math.Round((ram % 1) * 100), 0, 99);
        return report;
    }
}

internal static class ImageUpload {
 public static void SendExisting(HidStream stream,HidDevice d,string path,List<byte[]> layout) {
  byte[] data=File.ReadAllBytes(path);
  if (data.Length < 100 || data.Length > 2000000 || data[0]!=0xff || data[1]!=0xd8) throw new InvalidDataException("Invalid image");
  stream.WriteTimeout=1000; stream.ReadTimeout=3000;
  Console.WriteLine($"reports in={d.GetMaxInputReportLength()} out={d.GetMaxOutputReportLength()}, image={data.Length}");
  int pages=(data.Length+1011)/1012;
  for(int page=0;page<pages;page++){
   int len=Math.Min(1012,data.Length-page*1012); byte[] b=new byte[d.GetMaxOutputReportLength()];
   b[0]=1;b[1]=40;BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(2),(uint)data.Length);b[6]=(byte)(page>>16);b[7]=(byte)(page>>8);b[8]=(byte)page;BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(9),(ushort)(len+1));b[11]=255;Array.Copy(data,page*1012,b,12,len);
   stream.Write(b); var a=new byte[d.GetMaxInputReportLength()]; bool ok=false; var until=DateTime.UtcNow.AddSeconds(3);
   while(DateTime.UtcNow<until) { int n=stream.Read(a,0,a.Length); if(n>=9 && a[0]==1 && a[1]==40 && ((a[6]<<16)|(a[7]<<8)|a[8])==page){ok=true;break;} }
   if(!ok)throw new IOException($"No ACK for page {page}");
  }
  Console.WriteLine($"Uploaded {pages} pages");
  foreach (var packet in layout) { stream.Write(packet); Thread.Sleep(50); }
  Console.WriteLine("Reapplied validated LCD layout");
 }
}

internal static class Layout {
 public static List<byte[]> Prepare(int size,int ramGiB,int gpuMaxW) {
  string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Phanteks","configs.xml");
  var doc=System.Xml.Linq.XDocument.Load(path); var root=doc.Root ?? throw new InvalidDataException("Missing root");
  var profile=root.Elements("profile").Single(x=>(string?)x.Attribute("index")== (string?)root.Attribute("index"));
  var lcd=profile.Elements("lcd").Single(x=>(string?)x.Attribute("name")=="lcd6hd");
  return Build(lcd,size,ramGiB,gpuMaxW);
 }
 public static List<byte[]> Build(System.Xml.Linq.XElement lcd,int size,int ramGiB,int gpuMaxW) {
  int Int(System.Xml.Linq.XElement e,string key)=>int.Parse((string?)e.Attribute(key)??throw new InvalidDataException("Missing " + key));
  if(size<1024 || size>65536 || ramGiB<1 || ramGiB>255 || gpuMaxW<1 || gpuMaxW>65535) throw new InvalidDataException("Invalid report size or sensor limits");
  var items=lcd.Elements("item").ToArray();
  int[] modes={0,0,5,5,3,3}, sources={1,2,42,32,1,2};
  if(Int(lcd,"layoutIndex")!=9 || Int(lcd,"bgMode")!=0 || Int(lcd,"direction")!=0 || items.Length!=6) throw new InvalidDataException("Requires landscape LCD6-HD layout 9, image background and six widgets");
  var packets=new List<byte[]>(6);
  for(int i=0;i<items.Length;i++) {
   var e=items[i];
   if(Int(e,"widget")!=2 || Int(e,"widgetMode")!=modes[i] || Int(e,"index")!=i || Int(e,"source1")!=sources[i] || Int(e,"source2")!=1 || Int(e,"source3")!=1 || (string?)e.Attribute("isAlarm")!="off")
    throw new InvalidDataException("Widget configuration does not match the supported CPU/GPU/RAM/power layout; no changes made");
   if(i<2 && ((string?)e.Attribute("color5")!="64,64,64,255" || (string?)e.Attribute("color6")!="255,200,0,255"))
    throw new InvalidDataException("Unsupported gauge colors");
   byte[] b=new byte[size]; b[0]=1;b[1]=48;b[3]=6;b[4]=(byte)i;b[5]=9;b[8]=(byte)i;int o=11;
   string[] names={"widget","widgetMode","position","source1","source2","source3"};
   for(int j=0;j<names.Length;j++) b[o+j]=checked((byte)Int(e,names[j]));
   int src=sources[i],max=src==42?(ramGiB<<8)|86:src==32?gpuMaxW:0;
   BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(o+6),checked((ushort)max));
   BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(o+12),checked((ushort)Int(e,"alarmTempC"))); b[o+15]=checked((byte)Int(e,"frequency"));
   string[] keys={"text1","text2","text3"};int[] offsets={16,49,82};
   for(int j=0;j<3;j++) { string str=(string?)e.Attribute(keys[j])??""; if(str.Any(ch=>ch>127)) throw new InvalidDataException("Only ASCII widget labels are currently supported"); if(str.Length>32) str=str[..32];var bytes=Encoding.ASCII.GetBytes(str);b[o+offsets[j]]=(byte)bytes.Length;Array.Copy(bytes,0,b,o+offsets[j]+1,bytes.Length); }
   for(int j=0;j<6;j++) { var c=((string?)e.Attribute("color"+(j+1))??throw new InvalidDataException("Missing color")).Split(',').Select(byte.Parse).ToArray();if(c.Length!=4)throw new InvalidDataException("Invalid color"); int z=o+125+j*4;b[z]=c[3];b[z+1]=c[0];b[z+2]=c[1];b[z+3]=c[2]; }
   if(modes[i]==0) new byte[]{255,130,130,130,255,255,235,180}.CopyTo(b,o+165);
   packets.Add(b);
  }
  return packets;
 }
 public static void Test() {
  var lcd=System.Xml.Linq.XElement.Parse("<lcd layoutIndex='9' bgMode='0' direction='0'/>");
  int[] modes={0,0,5,5,3,3},sources={1,2,42,32,1,2};
  for(int i=0;i<6;i++) {
   var e=new System.Xml.Linq.XElement("item",new System.Xml.Linq.XAttribute("widget",2),new System.Xml.Linq.XAttribute("widgetMode",modes[i]),new System.Xml.Linq.XAttribute("index",i),new System.Xml.Linq.XAttribute("position",2),new System.Xml.Linq.XAttribute("source1",sources[i]),new System.Xml.Linq.XAttribute("source2",1),new System.Xml.Linq.XAttribute("source3",1),new System.Xml.Linq.XAttribute("isAlarm","off"),new System.Xml.Linq.XAttribute("alarmTempC",80),new System.Xml.Linq.XAttribute("frequency",1));
   for(int j=1;j<=6;j++) e.SetAttributeValue("color"+j,j==5?"64,64,64,255":j==6?"255,200,0,255":"255,255,255,255");lcd.Add(e);
  }
  var p=Build(lcd,1024,64,600);
  if(p.Count!=6 || p[0][1]!=48 || p[5][8]!=5 || BinaryPrimitives.ReadUInt16BigEndian(p[2].AsSpan(17))!=((64<<8)|86) || BinaryPrimitives.ReadUInt16BigEndian(p[3].AsSpan(17))!=600) throw new Exception("Layout packet test failed");
  lcd.Elements("item").First().SetAttributeValue("source1",99); bool rejected=false;
  try { Build(lcd,1024,64,600); } catch(InvalidDataException) { rejected=true; }
  if(!rejected)throw new Exception("Unsupported layout was not rejected");
  Console.WriteLine("PASS: layout packets and incompatible layout rejection");
 }
}
