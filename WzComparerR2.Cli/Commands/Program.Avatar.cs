using System;
using System.Collections.Generic;

namespace WzComparerR2.Cli
{
    internal static partial class Program
    {
        private static int RunAvatar(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintAvatarHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            if (subCommand == "render")
            {
                return RunAvatarRender(args);
            }

            if (subCommand != "inspect" && subCommand != "unpack")
            {
                throw new UsageException("Unknown avatar command: " + args.Positionals[0]);
            }

            string code = args.GetValue("code");
            bool json = args.HasFlag("json") || subCommand == "unpack";
            if (string.IsNullOrEmpty(code))
            {
                throw new UsageException("avatar " + subCommand + " requires --code <code>.");
            }

            var result = AvatarCodeDto.Parse(code);
            WriteOutput(result, json, writer =>
            {
                writer.WriteLine("Avatar code items: " + result.Items.Count);
                foreach (var item in result.Items)
                {
                    writer.WriteLine(item.Id + "\t" + item.Category);
                }
            });
            return ExitSuccess;
        }

        private static int RunAvatarRender(ParsedArgs args)
        {
            if (!args.HasFlag("dry-run"))
            {
                throw new UsageException("avatar render currently supports --dry-run only. Real PNG rendering needs AvatarCommon dependency isolation first.");
            }

            string output = args.GetValue("out");
            if (string.IsNullOrEmpty(output))
            {
                output = args.GetValue("output");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("avatar render requires --out <avatar.png>.");
            }

            var avatarCode = ParseAvatarRenderInput(args);
            if (!avatarCode.IsValid)
            {
                throw new UsageException("avatar render requires --code <code> or --items <ids>.");
            }

            string action = args.GetValue("action");
            if (string.IsNullOrEmpty(action))
            {
                action = "stand1";
            }

            string emotion = args.GetValue("emotion");
            if (string.IsNullOrEmpty(emotion))
            {
                emotion = "default";
            }

            var result = AvatarRenderPlanDto.Create(
                avatarCode,
                output,
                action,
                emotion,
                args.HasFlag("offline"),
                !string.IsNullOrEmpty(args.GetValue("api-key")));

            bool json = args.HasFlag("json") || args.HasFlag("dry-run");
            WriteOutput(result, json, writer =>
            {
                writer.WriteLine("Avatar render dry-run");
                writer.WriteLine("Output: " + result.OutputPath);
                writer.WriteLine("Action: " + result.Action + " Emotion: " + result.Emotion);
                writer.WriteLine("Items: " + result.Items.Count);
                foreach (var candidate in result.Candidates)
                {
                    writer.WriteLine(candidate.Id + "\t" + candidate.Category + "\t" + string.Join(", ", candidate.CandidatePaths));
                }
                foreach (string blocker in result.Blockers)
                {
                    writer.WriteLine("Blocker: " + blocker);
                }
            });

            return ExitSuccess;
        }

        private static AvatarCodeDto ParseAvatarRenderInput(ParsedArgs args)
        {
            var parts = new List<string>();
            string code = args.GetValue("code");
            if (!string.IsNullOrEmpty(code))
            {
                parts.Add(code);
            }

            string items = args.GetValue("items");
            if (!string.IsNullOrEmpty(items))
            {
                parts.Add(items);
            }

            for (int i = 1; i < args.Positionals.Count; i++)
            {
                parts.Add(args.Positionals[i]);
            }

            return AvatarCodeDto.Parse(string.Join(",", parts));
        }
    }
}
