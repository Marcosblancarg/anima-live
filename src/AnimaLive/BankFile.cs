using System.IO;
using System.IO.Compression;
using System.Text.Json;
namespace src;

public static class BankFile
{
    sealed class Bank { public int Version { get; set; } = 1; public Pad[] Pads { get; set; } = Array.Empty<Pad>(); }
    public static void Save(string path, Pad[] pads)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                using (var json = zip.CreateEntry("bank.json").Open()) JsonSerializer.Serialize(json, new Bank { Version = pads.Length == 64 ? 2 : 1, Pads = pads }, new JsonSerializerOptions { WriteIndented = true });
                for (int i = 0; i < pads.Length; i++) if (pads[i].Original.Length > 0) { using var target = zip.CreateEntry($"audio/{i:00}" + Path.GetExtension(pads[i].FileName), CompressionLevel.NoCompression).Open(); target.Write(pads[i].Original); }
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static Pad[] Load(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("bank.json") ?? throw new InvalidDataException("Este archivo no es un banco Anima Live Pad.");
        if (entry.Length > 1048576) throw new InvalidDataException("El banco contiene una configuración demasiado grande.");
        Bank bank; using (var stream = entry.Open()) bank = JsonSerializer.Deserialize<Bank>(stream) ?? throw new InvalidDataException("Banco vacío.");
        if (!((bank.Version == 1 && bank.Pads.Length == 16) || (bank.Version == 2 && bank.Pads.Length == 64))) throw new InvalidDataException("Versión de banco no compatible.");
        long total = 0;
        for (int i = 0; i < bank.Pads.Length; i++)
        {
            var p = bank.Pads[i] ?? throw new InvalidDataException("Pad inválido.");
            if (p.Name == null || p.Color == null || p.FileName == null || p.Note < 0 || p.Note > 127 || p.Channel < 0 || p.Channel > 16 || !Enum.IsDefined(p.Mode) || !double.IsFinite(p.GainDb) || p.GainDb < -48 || p.GainDb > 12) throw new InvalidDataException("Configuración de pad inválida.");
            if (p.FileName == "") continue;
            var audio = zip.GetEntry($"audio/{i:00}" + Path.GetExtension(p.FileName)) ?? throw new InvalidDataException($"Falta el audio del pad {i + 1}.");
            if (audio.Length > 256L * 1024 * 1024) throw new InvalidDataException("Un audio del banco supera 256 MB.");
            string temp = Path.Combine(Path.GetTempPath(), "airepads-" + Guid.NewGuid().ToString("N") + Path.GetExtension(p.FileName));
            try { using (var input = audio.Open()) using (var output = File.Create(temp)) input.CopyTo(output); p.Samples = Decoder.Read(temp); p.Original = File.ReadAllBytes(temp); } finally { if (File.Exists(temp)) File.Delete(temp); }
            total += p.Samples.Length;
            if (total > 268435456) throw new InvalidDataException("El banco supera 1 GB de audio decodificado.");
            if (!double.IsFinite(p.Start) || !double.IsFinite(p.End) || p.Start < 0 || p.End <= p.Start || p.End > p.Duration + 0.001) throw new InvalidDataException("El banco contiene cortes inválidos.");
            p.End = Math.Min(p.End, p.Duration);
        }
        var result = PadBank.Create(); Array.Copy(bank.Pads, result, bank.Pads.Length); return result;
    }
}
