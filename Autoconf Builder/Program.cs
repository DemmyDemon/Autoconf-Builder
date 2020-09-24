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
                SwapDictionaryFileWithLua(handler.start, dirName);
                SwapDictionaryFileWithLua(handler.stop, dirName);
                SwapDictionaryFileWithLua(handler.flush, dirName);
                SwapDictionaryFileWithLua(handler.update, dirName);
                SwapActionListFileWithLua(handler.actionStart, dirName);
                SwapActionListFileWithLua(handler.actionStop, dirName);
                SwapActionListFileWithLua(handler.actionLoop, dirName);
            }
        }

        static void SwapDictionaryFileWithLua(Dictionary<string, string> where, string dirName)
        {
            if (where == null){ return; }
            string fileName;
            if (where.TryGetValue("file", out fileName)){
                string fullFileName = dirName+'\\'+fileName;
                string lua = SlurpFile(fullFileName);
                if (where.ContainsKey("lua"))
                {
                    Console.WriteLine("WARNING: LUA ALREADY PRESENT WHEN INJECTING FILE!");
                    where["lua"] += Environment.NewLine + lua;
                }
                else
                {
                    where["lua"] = lua;
                }
            }
            where.Remove("file");
        }
        static void SwapActionListFileWithLua(List<AutoconfAction> where, string dirName)
        {
            if (where == null)
            {
                return;
            }
            foreach (AutoconfAction thisAction in where)
            {
                if (thisAction.file == null) { continue; }
                string lua = SlurpFile(dirName + '\\' + thisAction.file);
                thisAction.file = null;
                if (thisAction.lua == null)
                {
                    thisAction.lua = lua;
                }
                else
                {
                    Console.WriteLine("WARNING: LUA ALREADY PRESENT WHEN INJECTING FILE INTO ACTION!");
                    thisAction.lua += Environment.NewLine + lua;
                }
            }
        }
        
        static string SlurpFile(string fileName)
        {
            if (File.Exists(fileName))
            {
                string contents = File.ReadAllText(fileName);
                contents = Regex.Replace(contents, @"^\s*", string.Empty, RegexOptions.Multiline); // Remove indentation
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
                //.WithNamingConvention(CamelCaseNamingConvention.Instance)
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
            public Dictionary<string, string> start { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, string> stop { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, string> flush { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, string> update { get; set; } = new Dictionary<string, string>();
            public List<AutoconfAction> actionStart { get; set; } = new List<AutoconfAction>();
            public List<AutoconfAction> actionStop { get; set; } = new List<AutoconfAction>();
            public List<AutoconfAction> actionLoop { get; set; } = new List<AutoconfAction>();

            public void Read(IParser parser, Type expectedType, ObjectDeserializer nestedObjectDeserializer)
            {

                parser.Consume<MappingStart>();
                Scalar node;
                while (parser.TryConsume(out node))
                {
                    // Console.WriteLine(node);
                    switch (node.Value)
                    {
                        case "start":
                            start = (Dictionary<string, string>)nestedObjectDeserializer(start.GetType());
                            break;
                        case "stop":
                            stop = (Dictionary<string, string>)nestedObjectDeserializer(stop.GetType());
                            break;
                        case "flush":
                            flush = (Dictionary<string, string>)nestedObjectDeserializer(flush.GetType());
                            break;
                        case "update":
                            update = (Dictionary<string, string>)nestedObjectDeserializer(update.GetType());
                            break;
                        case "actionStart":
                            AutoconfAction actionStartNode = (AutoconfAction)nestedObjectDeserializer(typeof(AutoconfAction));
                            actionStart.Add(actionStartNode);
                            break;
                        case "actionStop":
                            AutoconfAction actionStopNode = (AutoconfAction)nestedObjectDeserializer(typeof(AutoconfAction));
                            actionStop.Add(actionStopNode);
                            break;
                        case "actionLoop":
                            AutoconfAction actionLoopNode = (AutoconfAction)nestedObjectDeserializer(typeof(AutoconfAction));
                            actionLoop.Add(actionLoopNode);
                            break;
                        default:
                            throw new NotImplementedException($"I have no idea what to do with {node.Value} nodes");

                    }
                }
                parser.Consume<MappingEnd>();

            }

            public void Write(IEmitter emitter, ObjectSerializer nestedObjectSerializer)
            {
                emitter.Emit(new MappingStart(null, null, true, MappingStyle.Block));
                foreach (var tag in new string[4]{ "start", "stop", "flush", "update" }){
                    var dict = (Dictionary<string, string>) this.GetType().GetProperty(tag).GetValue(this, null);
                    
                    if (dict == null) { continue; }
                    if (dict.Count == 0) { continue; }

                    emitter.Emit(new Scalar(tag));
                    nestedObjectSerializer(dict, dict.GetType());
                }
                foreach (var action in new string[3] { "actionStart", "actionStop", "actionLoop" })
                {
                    var list = (List<AutoconfAction>)this.GetType().GetProperty(action).GetValue(this, null);

                    if (list == null) { continue; }
                    if (list.Count == 0) { continue; }

                    foreach (var thisAction in list) {
                        emitter.Emit(new Scalar(action));
                        nestedObjectSerializer(thisAction, thisAction.GetType());
                    }
                }
                emitter.Emit(new MappingEnd());
            }
        }

        public class AutoconfAction
        {
            public List<string> args { get; set; }

            [YamlMember(ScalarStyle = ScalarStyle.Literal)]
            public string lua { get; set; }
            public string file { get; set; }
        }
    }
}
