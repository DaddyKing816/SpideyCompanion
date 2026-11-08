// ============================================================================
//  A tiny build-time tool (not shipped): reassembles the buddy's PNG poses
//  from poses-idat.json at build time.
//  Why: the GitHub snapshot tooling used for this repo accepts UTF-8 text
//  only, so the poses travel as JSON - per pose, the compressed pixel
//  stream (the PNG IDAT chunk payload) plus the small header/trailer
//  chunks, all base64. A PNG is length-prefixed chunks, so the original
//  file is rebuilt exactly: head + length + "IDAT" + data + trailer,
//  byte-for-byte identical (verified: 46 frames, 0 byte differences).
//  Art: the Shimeji Spider-Man poses by haydentheartist (Reddit).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

internal static class PoseTool
{
    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: PoseTool <outdir> <poses-idat.json> [more.json ...]");
            return 2;
        }
        Directory.CreateDirectory(args[0]);
        int count = 0;
        var meta = new Dictionary<string, string[]>();
        for (int a = 1; a < args.Length; a++)
        {
            string json = File.ReadAllText(args[a]);
            string dataSec = Section(json, "\"data\"");
            string metaSec = Section(json, "\"meta\"");
            foreach (Match m in Regex.Matches(dataSec, "\"(shime\\d+\\.png)\"\\s*:\\s*\"([A-Za-z0-9+/=]+)\""))
                Data[m.Groups[1].Value] = Convert.FromBase64String(m.Groups[2].Value);
            foreach (Match m in Regex.Matches(metaSec, "\"(shime\\d+\\.png)\"\\s*:\\s*\\[\\s*\"([A-Za-z0-9+/=]*)\"\\s*,\\s*\"([A-Za-z0-9+/=]*)\"\\s*\\]"))
                meta[m.Groups[1].Value] = new[] { m.Groups[2].Value, m.Groups[3].Value };
        }

        foreach (var kv in Data)
        {
            byte[] head = Convert.FromBase64String(meta[kv.Key][0]);
            byte[] rest = Convert.FromBase64String(meta[kv.Key][1]);
            byte[] idat = kv.Value;
            using (var fs = new FileStream(Path.Combine(args[0], kv.Key), FileMode.Create))
            {
                fs.Write(head, 0, head.Length);
                fs.Write(new[] { (byte)(idat.Length >> 24), (byte)(idat.Length >> 16), (byte)(idat.Length >> 8), (byte)idat.Length }, 0, 4);
                byte[] tag = { (byte)'I', (byte)'D', (byte)'A', (byte)'T' };
                fs.Write(tag, 0, 4);
                fs.Write(idat, 0, idat.Length);
                fs.Write(rest, 0, rest.Length);
            }
            count++;
        }
        Console.WriteLine("poses rebuilt: " + count);
        return count == 46 ? 0 : 1;
    }

    private static readonly Dictionary<string, byte[]> Data = new Dictionary<string, byte[]>();

    private static string Section(string json, string key)
    {
        int i = json.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return "";
        int start = json.IndexOf('{', i);
        int depth = 0;
        for (int j = start; j < json.Length; j++)
        {
            if (json[j] == '{') depth++;
            else if (json[j] == '}' && --depth == 0) return json.Substring(start, j - start + 1);
        }
        return "";
    }
}
