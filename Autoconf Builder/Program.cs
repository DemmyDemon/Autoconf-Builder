using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Core;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization.EventEmitters;

namespace Autoconf_Builder
{
    class FlowStyleIntegerSequences : ChainedEventEmitter
    {
        public FlowStyleIntegerSequences(IEventEmitter nextEmitter)
            : base(nextEmitter) { }

        public override void Emit(SequenceStartEventInfo eventInfo, IEmitter emitter)
        {
            if (typeof(IEnumerable<string>).IsAssignableFrom(eventInfo.Source.Type))
            {
                eventInfo = new SequenceStartEventInfo(eventInfo.Source)
                {
                    Style = SequenceStyle.Flow
                };
            }

            nextEmitter.Emit(eventInfo, emitter);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            if ( args.Length > 0) {
                if (File.Exists(args[0]))
                {
                    string dirName = Path.GetDirectoryName(args[0]);
                    Autoconf data = SlurpYaml(args[0]);
                    Console.WriteLine($"AutoConf found: {data.name}");
                    FindAndInjectLua(data, dirName);

                    string outDir = dirName;

                    if (args.Length > 1)
                    {
                        if (Directory.Exists(args[1]))
                        {
                            outDir = args[1];
                        }
                        else
                        {
                            Console.WriteLine($"Output directory does not exist: {args[1]}");
                            Environment.Exit(1);
                        }
                    }

                    string outFile = outDir + '\\' + data.name.ToLower().Trim().Replace(' ', '_') + ".conf";
                    var serializer = new SerializerBuilder()
                        .WithEventEmitter(next => new FlowStyleIntegerSequences(next))
                        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                        .Build();
                    var yaml = serializer.Serialize(data);
                    File.WriteAllText(outFile, yaml);
                    Console.WriteLine($"Written to {outFile}");
                }
                else
                {
                    Console.WriteLine($"The file specified does not exist: {args[0]}");
                    Environment.Exit(1);
                }
            }
            else
            {
                Console.WriteLine("Please specify a YAML file to build from.");
                Environment.Exit(1);
            }
        }

        static void FindAndInjectLua(Autoconf data, string dirName)
        {
            foreach (AutoconfHandler handler in data.handlers.Values)
            {
                foreach (AutoconfFilter filter in handler.filters)
                {
                    if (filter.file != null && filter.file.Length > 0)
                    {
                        if (filter.lua.Length > 0)
                        {
                            filter.lua += Environment.NewLine;
                        }
                        filter.lua += SlurpFile(dirName + '\\' + filter.file);
                    }
                    filter.file = null;

                    if (filter.files != null && filter.files.Count > 0)
                    {
                        foreach (string fileName in filter.files)
                        {
                            if (filter.lua.Length > 0)
                            {
                                filter.lua = Regex.Replace(filter.lua, @"\r?\n$", string.Empty);
                                filter.lua += Environment.NewLine;
                            }
                            filter.lua += SlurpFile(dirName + '\\' + fileName);
                        }
                    }
                    filter.files = null;

                    if (filter.args.Count == 0)
                    {
                        filter.args = null;
                    }
                }
            }
        }
        static string SlurpFile(string fileName)
        {
            if (File.Exists(fileName))
            {
                string contents = File.ReadAllText(fileName);
                // contents = Regex.Replace(contents, @"^\s*", string.Empty, RegexOptions.Multiline); // Remove indentation
                contents = Regex.Replace(contents, @"^--.*$", string.Empty, RegexOptions.Multiline); // Remove comments
                contents = Regex.Replace(contents, @"^\s*$[\r?\n]*", string.Empty, RegexOptions.Multiline); // Remove empty lines
                contents = Regex.Replace(contents, @"\s*$", string.Empty, RegexOptions.Multiline); // Remove trailing whitepace
                return contents;
            }
            else
            {
                Console.WriteLine($"File not found: {fileName}");
                Environment.Exit(1);
            }
            return "-- SOMETHING ERRROR HAPPEN!\n"; // Will never run, but compiler requires string return even for the Exit(1) code path.
        }

        static Autoconf SlurpYaml(string fileName)
        {
            StreamReader fileContents = File.OpenText(fileName);
            var deserializer = new DeserializerBuilder()
                .Build();
            var data = deserializer.Deserialize<Autoconf>(fileContents);
            fileContents.Close();

            return data;
        }

        public class Autoconf
        {
            public string name { get; set; }
            public Dictionary<string, AutoconfSlot> slots { get; set; }
            public Dictionary<string, AutoconfHandler> handlers { get; set; }
        }

        public class AutoconfSlot
        {
            [YamlMember(Alias = "class")]
            public string className { get; set; }
            public string select { get; set; }
        }
        public class AutoconfHandler : IYamlConvertible
        {
            public List<AutoconfFilter> filters { get; set; } = new List<AutoconfFilter>();
            public void Read(IParser parser, Type expectedType, ObjectDeserializer nestedObjectDeserializer)
            {

                parser.Consume<MappingStart>();
                Scalar node;
                while (parser.TryConsume(out node))
                {
                    AutoconfFilter thisFilter = (AutoconfFilter)nestedObjectDeserializer(typeof(AutoconfFilter));
                    thisFilter.name = node.Value;
                    filters.Add(thisFilter);
                }
                parser.Consume<MappingEnd>();

            }

            public void Write(IEmitter emitter, ObjectSerializer nestedObjectSerializer)
            {
                emitter.Emit(new MappingStart(null, null, true, MappingStyle.Block));
                foreach(AutoconfFilter filter in filters)
                {
                    emitter.Emit(new Scalar(filter.name));
                    nestedObjectSerializer(filter, typeof(AutoconfFilter));
                }
                emitter.Emit(new MappingEnd());
            }
        }
        public class AutoconfFilter
        {
            [YamlMember(ScalarStyle = ScalarStyle.Literal)]
            public string lua { get; set; } = "";

            [YamlIgnore]
            public string name { get; set; } = "unknown";

            public List<string> args { get; set; } = new List<string>();
            public string file { get; set; }
            public List<string> files { get; set; }
        }
    }
}
