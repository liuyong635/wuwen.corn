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
    public class StopWorkHander : SignalHandlerBase
    {
        public override string HandlerId => "StopWorkHander";
        public const string SIG_STOP_WORKFLOW = "StopWorkFlow";
        public override string HandlerName => "停止流程";
        public override string[] DependentDevices { get; } = new string[] { "PLC" };
        public override ITriggerCondition TriggerCondition => When.All(
            When.IsRunning(),
            When.FlagOn("WorkAbort"),
            When.FlagOff("WorkAborting")
        );


        protected override async Task<(bool, string)> ExecuteAsync(IHandlerContext ctx, CancellationToken ct)
        {
            FlagCondition.SetFlag("WorkAborting", true);
            WriteSignal(ctx, SIG_STOP_WORKFLOW, true);
            await Task.Delay(2500);
            WriteSignal(ctx, SIG_STOP_WORKFLOW, false);
            return Success();
        }
    }
}
