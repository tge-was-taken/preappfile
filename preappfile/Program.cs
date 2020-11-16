using CommandLine;
using GlobExpressions;
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
            [Option( 'i', "input", Required = true, HelpText = "Input file/directory path." )]
            public string InputPath { get; set; }

            [Option( 'o', "output", Required = true, HelpText = "Output file/directory path." )]
            public string OutputPath { get; set; }

            [Option( 'c', "compress", Required = false, HelpText = "Toggle file compression.", Default = false )]
            public bool Compress { get; set; }

            [Option( 'a', "append", Required = false, HelpText = "Archive to append files to." )]
            public string AppendPath { get; set; }

            [Option( "unpack-filter", Required = false, HelpText = "Glob pattern that file paths must match to be eligible for extracting." )]
            public string UnpackFilter { get; set; }
        }

        private static Glob sUnpackFilterGlob;

        static int Main( string[] args )
        {
            var parserArgs = PreProcessArgs( args );
            var result = Parser.Default.ParseArguments<Options>( parserArgs );
            return result.MapResult(
                options => Run( options ),
                _ => 1 );
        }

        static IList<string> PreProcessArgs( string[] args )
        {
            IList<string> parserArgs = args;

            if ( args.Length > 0 )
            {
                // Hack in implicit input/output args
                if ( File.Exists( args[ 0 ] ) )
                {
                    parserArgs = args.ToList();
                    parserArgs.Insert( 0, "-i" );
                    if ( !parserArgs.Contains( "-o" ) || !parserArgs.Contains( "--output" ) )
                    {
                        parserArgs.Add( "-o" );
                        var outPath = Path.Combine( Path.GetDirectoryName( args[ 0 ] ), Path.GetFileNameWithoutExtension( args[ 0 ] ) );
                        parserArgs.Add( outPath );
                    }
                }
                else if ( Directory.Exists( args[ 0 ] ) )
                {
                    parserArgs = args.ToList();
                    parserArgs.Insert( 0, "-i" );
                    if ( !parserArgs.Contains( "-o" ) || !parserArgs.Contains( "--output" ) )
                    {
                        parserArgs.Add( "-o" );
                        parserArgs.Add( args[ 0 ] + ".cpk" );
                    }
                }
            }

            return parserArgs;
        }

        static void PreProcessOptions(Options options)
        {
            if ( options.UnpackFilter != null && sUnpackFilterGlob == null )
                sUnpackFilterGlob = new Glob( options.UnpackFilter, GlobOptions.CaseInsensitive | GlobOptions.Compiled );
        }

        static int Run( Options options )
        {
            PreProcessOptions( options );

            if ( File.Exists( options.InputPath ) )
            {
                // Unpack archive
                var ext = Path.GetExtension( options.InputPath ).ToLowerInvariant();
                if ( ext == ".cpk" )
                {
                    return UnpackCpk( options );
                }
                else if ( ext == ".pac" )
                {
                    // Extract pac
                    return UnpackPac( options );
                }
                else
                {
                    Console.WriteLine( $"Unknown input file format: {ext}" );
                }
            }
            else if ( Directory.Exists( options.InputPath ) )
            {
                // Pack archive
                var ext = Path.GetExtension( options.OutputPath ).ToLowerInvariant();
                if ( ext == ".cpk" )
                {
                    return PackCpk( options );
                }
                else if ( ext == ".pac" )
                {
                    return PackPac( options );
                }
                else
                {
                    Console.WriteLine( $"Unknown output file format: {ext}" );
                }
            }

            return 1;
        }

        static bool ShouldUnpack( string path )
        {
            if ( sUnpackFilterGlob == null ) return true;
            return sUnpackFilterGlob.IsMatch( path );
        }

        static int UnpackCpk( Options options )
        {
            var name = Path.GetFileNameWithoutExtension( options.InputPath );
            var dir = Path.GetDirectoryName( options.InputPath );

            // Try to detect pac base name
            var pacBaseName = GetPacBaseNameFromCpkBaseName( dir, name );
            var cpk = new CpkFile( options.InputPath );

            // Load needed pacs
            var packs = new List<DwPackFile>();
            foreach ( var pacIdx in cpk.Entries.Select( x => x.PacIndex ).Distinct().OrderBy( x => x ) )
            {
                var pacName = $"{pacBaseName}{pacIdx:D5}.pac";
                var pacPath = Path.Combine( dir, pacName );
                if ( !File.Exists( pacPath ) )
                {
                    Console.WriteLine( $"Failed to unpack: Missing {pacName}" );
                    return 1;
                }

                var pac = new DwPackFile( pacPath );
                var refFileCount = cpk.Entries.Where( x => x.PacIndex == pacIdx )
                    .Select( x => x.FileIndex )
                    .Max() + 1;
                if ( refFileCount > pac.Entries.Count )
                {
                    Console.WriteLine( $"Failed to unpack: CPK references {refFileCount} in {pacName} but only {pac.Entries.Count} exist." );
                    return 1;
                }

                packs.Add( pac );
            }

            cpk.Unpack( packs, options.OutputPath, ( e =>
            {
                if ( !ShouldUnpack( e.Path ) ) return false;
                Console.WriteLine( $"Extracting {e.Path} (pac: {e.PacIndex}, file: {e.FileIndex})" );
                return true;
            } ) );
            return 0;
        }

        private static string FormatPacName( string baseName, int index )
        {
            return $"{baseName}{index:D5}.pac";
        }

        private static string GetPacBaseNameFromCpkBaseName( string dir, string baseName )
        {
            var pacBaseName = baseName;
            if ( !File.Exists( Path.Combine( dir, FormatPacName( pacBaseName, 0 ) ) ) )
            {
                // Trim language suffix: _e, _c, _k
                var start = pacBaseName.IndexOf( '_' );
                if ( start != -1 )
                {
                    pacBaseName = pacBaseName.Substring( 0, start );
                }
            }

            return pacBaseName;
        }

        static int UnpackPac( Options options )
        {
            var pack = new DwPackFile( options.InputPath );
            pack.Unpack( options.OutputPath, ( entry =>
            {
                if ( !ShouldUnpack( entry.Path ) ) return false;
                Console.WriteLine( $"Extracting {entry.Path}" );
                return true;
            } ) );
            return 0;
        }

        static int PackCpk( Options options )
        {
            var cpkName = Path.GetFileName( options.InputPath );
            var cpkBaseName = Path.GetFileNameWithoutExtension( options.InputPath );
            var cpkDir = Path.GetDirectoryName( options.OutputPath );
            CpkFile cpk;

            Console.WriteLine( "Creating CPK" );
            if ( options.AppendPath == null )
            {
                cpk = CpkFile.Pack( options.InputPath, options.Compress,
                    ( p =>
                    {
                        Console.WriteLine( $"{cpkName}: Adding {p}" );
                        return true;
                    } ),
                    ( pack =>
                    {
                        var pacName = FormatPacName( cpkBaseName, pack.Index );
                        var pacPath = Path.Combine( cpkDir, pacName );
                        using var pacFile = File.Create( pacPath );
                        Console.WriteLine( $"Creating {pacName}" );
                        pack.Write( pacFile, options.Compress,
                            ( p => Console.WriteLine( $"{pacName}: Writing {p.Path}" ) ) );
                    } ) );
            }
            else
            {
                Console.WriteLine( $"Appending to: {options.AppendPath}" );
                Console.WriteLine( $"Loading CPK: {options.AppendPath}" );
                cpk = new CpkFile( options.AppendPath );

                // Create new PAC
                var newPacIndex = cpk.Entries.Select( x => x.PacIndex ).Max() + 1;
                var pacName = FormatPacName( $"{GetPacBaseNameFromCpkBaseName( cpkDir, cpkBaseName )}", newPacIndex );

                Console.WriteLine( "Creating PAC: {pacName}" );
                var pac = DwPackFile.Pack( options.InputPath, newPacIndex, options.Compress, ( e =>
                {
                    Console.WriteLine( $"Adding {e}" );
                    return true;
                }));
                var pacPath = Path.Combine( cpkDir, pacName );
                using var packFile = File.Create( pacPath );
                pac.Write( packFile, options.Compress, ( e => Console.WriteLine( $"Writing {e.Path}" ) ) );

                // Add entries to CPK
                for ( var i = 0; i < pac.Entries.Count; i++ )
                {
                    var entry = pac.Entries[ i ];
                    cpk.Entries.Add( new CpkFileEntry( entry.Path, (short)i, (short)newPacIndex ) );
                }
            }

            // Write CPK
            Console.WriteLine( $"Writing CPK" );
            using var cpkFile = File.Create( options.OutputPath );
            cpk.Write( cpkFile );
            return 0;
        }

        static int PackPac( Options options )
        {
            DwPackFile pack;

            if ( options.AppendPath == null )
            {
                Console.WriteLine( $"Creating PAC: {Path.GetFileName( options.OutputPath )}" );
                pack = DwPackFile.Pack( options.InputPath, 0, options.Compress, ( p =>
                {
                    Console.WriteLine( $"Adding {p}" );
                    return true;
                } ) );
            }
            else
            {
                Console.WriteLine( $"Appending to PAC: { options.OutputPath }" );
                pack = new DwPackFile( options.AppendPath );
                pack.AddFiles( options.InputPath, options.Compress, ( p =>
                {
                    Console.WriteLine( $"Adding {p}" );
                    return true;
                } ) );
            }

            Console.WriteLine( $"Writing PAC" );
            using var packFile = File.Create( options.OutputPath );
            pack.Write( packFile, options.Compress, ( e => Console.WriteLine( $"Writing {e.Path}" ) ) );
            return 0;
        }
    }
}
