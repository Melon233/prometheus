using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PromeArchTrial.BattleServer.Configuration;
using PromeArchTrial.BattleServer.Diagnostics;
using PromeArchTrial.BattleServer.Networking;
using PromeArchTrial.Core.Networking;

namespace PromeArchTrial.BattleServer
{
    /// <summary>
    /// 加载 Character 1001 的服务端 Luban 生成物，并为正常运行和自动联调验收建立明确生命周期边界。
    /// </summary>
    internal static class Program
    {
        private const int DefaultCharacterId = 1001;

        /// <summary>启动全局唯一的 30 Hz 权威战斗世界，或在传入 --smoke-test 时执行完整自动验收。</summary>
        private static async Task<int> Main(string[] args)
        {
            try
            {
                string configDirectory = GetOptionValue(args, "--config-dir") ?? Path.Combine(AppContext.BaseDirectory, "ConfigData", "Luban");
                BattleServerConfiguration configuration = BattleServerConfiguration.Load(configDirectory, DefaultCharacterId);
                if (HasArgument(args, "--smoke-test")) return await SmokeTestRunner.RunAsync(configuration).ConfigureAwait(false);
                using (CancellationTokenSource shutdown = new CancellationTokenSource())
                using (BattleServerHost server = new BattleServerHost(BattleProtocol.DefaultPort, configuration.CharacterId, configuration.CharacterConfig))
                {
                    Console.CancelKeyPress += (_, eventArgs) =>
                    {
                        eventArgs.Cancel = true;
                        shutdown.Cancel();
                    };
                    Console.WriteLine("PromeArchTrial authoritative battle server starting. Press Ctrl+C to stop.");
                    Console.WriteLine($"Protocol=v{BattleProtocol.Version}, Character={configuration.CharacterId}, Hash=0x{configuration.ContentHash:X16}, TickRate={configuration.CharacterConfig.TickRate}, Config={configuration.DataDirectory}.");
                    await server.RunAsync(shutdown.Token).ConfigureAwait(false);
                }
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Battle server terminated: {exception}");
                return 1;
            }
        }

        /// <summary>以不区分大小写的方式判断命令行是否包含指定开关。</summary>
        private static bool HasArgument(string[] args, string expected)
        {
            for (int index = 0; index < args.Length; index++)
            {
                if (string.Equals(args[index], expected, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>读取 --name=value 或 --name value 形式的命令行选项，未提供时返回空。</summary>
        private static string GetOptionValue(string[] args, string optionName)
        {
            string prefix = optionName + "=";
            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return argument.Substring(prefix.Length);
                if (string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length) throw new ArgumentException($"Command-line option {optionName} requires a value.", nameof(args));
                    return args[index + 1];
                }
            }
            return null;
        }
    }
}
