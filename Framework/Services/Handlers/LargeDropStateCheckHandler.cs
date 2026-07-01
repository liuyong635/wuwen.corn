using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Handlers
{
    public class LargeDropStateCheckHandler : SignalHandlerBase
    {
        public override string HandlerId => "LargeDropStateCheckHandler";

        public override string HandlerName => "大料掉落检测";

        public const string LARGE_DROP_SIGNEL = "Large_drop_signel";

        public override string[] DependentDevices => new[] { "PLC", "Vision" };

        public override ITriggerCondition TriggerCondition => When.All(
              When.IsRunning(),
              When.SignalOn(LARGE_DROP_SIGNEL),
              When.FlagOff("Large_drop_signel_busy")
          );
        protected override Task<(bool, string)> ExecuteAsync(IHandlerContext context, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}
