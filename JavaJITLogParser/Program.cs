using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace JavaJITLogParser
{
    class Program
    {
        static bool SafeRead(XmlReader reader)
        {
            try
            {
                return reader.Read();
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void AppendMethodToStat(
            string type,
            string methodField,
            XmlReader reader,
            Dictionary<string, uint> trapsStat,
            Dictionary<string, uint> deoptStat)
        {
            var element = XNode.ReadFrom(reader) as XElement;
            AppendMethodToStat(type, methodField, element, trapsStat, deoptStat);
        }

        static void AppendMethodToStat(
            string type,
            string methodField,
            XElement element,
            Dictionary<string, uint> trapsStat,
            Dictionary<string, uint> deoptStat)
        {
            var stat = type == "uncommon_trap" ? trapsStat : deoptStat;
            string method = element.Attribute(methodField).Value;
            if (stat.ContainsKey(method))
            {
                stat[method]++;
            }
            else
            {
                stat[method] = 0;
            }
        }

        static void Parse(FileInfo input, FileInfo outputDeoptimized, FileInfo outputUncommonTraps)
        {
            var deoptStat = new Dictionary<string, uint>();
            var trapStat = new Dictionary<string, uint>();

            using (XmlReader reader = XmlReader.Create(input.FullName))
            {
                reader.MoveToContent();

                while (SafeRead(reader))
                {
                    if (reader.NodeType != XmlNodeType.Element)
                        continue;

                    if (reader.Name == "deoptimized" || reader.Name == "uncommon_trap")
                    {
                        string type = reader.Name;

                        if (type == "uncommon_trap")
                        {
                            var element = XNode.ReadFrom(reader) as XElement;
                            string reason = element.Attribute("reason").Value;
                            if (reason == "transfer_to_interpreter")
                                AppendMethodToStat(type, "jvmci_mirror_name", element, trapStat, deoptStat);
                        }

                        while (SafeRead(reader))
                        {
                            if (reader.NodeType == XmlNodeType.EndElement)
                                break;

                            if (reader.NodeType != XmlNodeType.Element)
                                continue;

                            if (reader.Name == "jvms")
                                AppendMethodToStat(type, "method", reader, trapStat, deoptStat);
                        }
                    }
                }
            }

            Dictionary<string, uint> orderedDeopt = deoptStat
                .OrderByDescending(x => x.Value)
                .ToDictionary(x => x.Key, x => x.Value);

            Dictionary<string, uint> orderedTrap = trapStat
                .OrderByDescending(x => x.Value)
                .ToDictionary(x => x.Key, x => x.Value);

            string serializedDeopt = JsonSerializer.Serialize(orderedDeopt);
            string serializedTrap = JsonSerializer.Serialize(orderedTrap);

            File.WriteAllText(outputDeoptimized.FullName, serializedDeopt);
            File.WriteAllText(outputUncommonTraps.FullName, serializedTrap);
        }

        static int Main(string[] args)
        {
            var rootCommand = new RootCommand
            {
                new Option<FileInfo>(
                    new[] {"--input", "-i"},
                    "Path to jit log file"
                )
                {
                    IsRequired = true
                },
                new Option<FileInfo>(
                    new[] {"--output-deoptimized", "-od"},
                    description: "Path to output file for deoptimized methods stat",
                    getDefaultValue:() => new FileInfo("outputDeoptimizedStat.txt")
                ),
                new Option<FileInfo>(
                    new[] {"--output-uncommon-traps", "-ot"},
                    description: "Path to output file for uncommon traps stat",
                    getDefaultValue:() => new FileInfo("outputUncommonTrapsStat.txt")
                )
            };

            rootCommand.Description = "Java JIT Log decompiled methods counter";

            rootCommand.Handler = CommandHandler.Create<FileInfo, FileInfo, FileInfo>(Parse);

            return rootCommand.InvokeAsync(args).Result;
        }
    }
}