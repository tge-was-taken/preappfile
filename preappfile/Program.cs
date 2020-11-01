using CommandLine;
using PreappPartnersLib.FileSystem;
using PreappPartnersLib.FileSystems;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace preappfile
{
    class Program
    {
        class Options
        {
            [Option('i', "input", Required = true, HelpText = "Input file/directory path.")]
            public string Input { get; set; }

            [Option('o', "output", Required = true, HelpText = "Output file/directory path.")]
            public string Output { get; set; }

            [Option('c', "compress", Required = false, HelpText = "Toggle file compression.", Default = false)]
            public bool Compress { get; set; }
        }

        static int Main(string[] args)
        {
            var parserArgs = PreProcessArgs(args);
            var result = Parser.Default.ParseArguments<Options>(parserArgs);
            return result.MapResult(
                options => Run(options),
                _ => 1);
        }

        static IList<string> PreProcessArgs(string[] args)
        {
            IList<string> parserArgs = args;

            if (args.Length > 0)
            {
                // Hack in implicit input/output args
                if (File.Exists(args[0]))
                {
                    parserArgs = args.ToList();
                    parserArgs.Insert(0, "-i");
                    if (!parserArgs.Contains("-o") || !parserArgs.Contains("--output"))
                    {
                        parserArgs.Add("-o");
                        var outPath = Path.Combine(Path.GetDirectoryName(args[0]), Path.GetFileNameWithoutExtension(args[0]));
                        parserArgs.Add(outPath);
                    }
                }
                else if (Directory.Exists(args[0]))
                {
                    parserArgs = args.ToList();
                    parserArgs.Insert(0, "-i");
                    if (!parserArgs.Contains("-o") || !parserArgs.Contains("--output"))
                    {
                        parserArgs.Add("-o");
                        parserArgs.Add(args[0] + ".cpk");
                    }
                }
            }

            return parserArgs;
        }

        static int Run(Options options)
        {
            if (File.Exists(options.Input))
            {
                // Unpack archive
                var ext = Path.GetExtension(options.Input).ToLowerInvariant();
                if (ext == ".cpk")
                {
                    return UnpackCpk(options);
                }
                else if (ext == ".pac")
                {
                    // Extract pac
                    return UnpackPac(options);
                }
                else
                {
                    Console.WriteLine($"Unknown input file format: {ext}");
                }
            }
            else if (Directory.Exists(options.Input))
            {
                // Pack archive
                var ext = Path.GetExtension(options.Output).ToLowerInvariant();
                if (ext == ".cpk")
                {
                    return PackCpk(options);
                }
                else if (ext == ".pac")
                {
                    return PackPac(options);
                }
                else
                {
                    Console.WriteLine($"Unknown output file format: {ext}");
                }
            }

            return 1;
        }

        static int UnpackCpk(Options options)
        {
            var name = Path.GetFileNameWithoutExtension(options.Input);
            var dir = Path.GetDirectoryName(options.Input);

            var cpk = new CpkFile(options.Input);
            var packs = new List<DwPackFile>();
            foreach (var file in Directory.EnumerateFiles(dir, $"{name}*.pac"))
                packs.Add(new DwPackFile(file));

            cpk.Unpack(packs, options.Output, (e => Console.WriteLine($"Extracting {e.Path} (pac: {e.PacIndex}, file: {e.FileIndex})")));
            return 0;
        }

        static int UnpackPac(Options options)
        {
            var pack = new DwPackFile(options.Input);
            pack.Unpack(options.Output, (entry => Console.WriteLine($"Extracting {entry.Path}")));
            return 0;
        }

        static int PackCpk(Options options)
        {
            var cpkName = Path.GetFileName(options.Input);
            var cpkBaseName = Path.GetFileNameWithoutExtension(options.Input);
            var cpkDir = Path.GetDirectoryName(options.Output);

            var cpk = CpkFile.Pack(options.Input, options.Compress,
                (p => Console.WriteLine($"{cpkName}: Adding {p}")),
                (pack =>
                {
                    var pacName = $"{cpkBaseName}{pack.Index:D5}.pac";
                    var pacPath = Path.Combine(cpkDir, pacName);
                    using var pacFile = File.Create(pacPath);
                    Console.WriteLine($"Creating {pacName}");
                    pack.Write(pacFile, options.Compress, 
                        (p => Console.WriteLine($"{pacName}: Writing {p.Path}")));
                }));

            using var cpkFile = File.Create(options.Output);
            cpk.Write(cpkFile);
            return 0;
        }

        static int PackPac(Options options)
        {
            var pack = DwPackFile.Pack(options.Input, options.Compress, (p => Console.WriteLine($"Adding {p}")));
            using var packFile = File.Create(options.Output);
            pack.Write(packFile, options.Compress, (e => Console.WriteLine($"Writing {e.Path}")));
            return 0;
        }
    }
}
