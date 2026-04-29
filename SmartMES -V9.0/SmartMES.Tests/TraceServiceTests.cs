using SmartMES.Core.Traceability;

namespace SmartMES.Tests;

public class TraceServiceTests
{
    [Fact]
    public void StartTraceAndProcess_ShouldRecordAndQuery()
    {
        var trace = new TraceService();

        trace.StartTrace("SN001", "LOT1", "PA001");
        var proc = trace.StartProcess("SN001", "视觉检测", "ST-01", "CAM-01");
        trace.AddParameter(proc.Id, "score", "98.6");
        trace.EndProcess(proc.Id, true, "OK");

        var t = trace.GetTrace("SN001");

        Assert.NotNull(t);
        Assert.Single(t!.Processes);
        Assert.True(t.IsPass);
        Assert.Equal("98.6", t.Processes[0].Parameters["score"]);
    }

    [Fact]
    public void Query_ShouldFilterByResult()
    {
        var trace = new TraceService();

        trace.StartTrace("SN_OK", "LOT1", "P");
        var okProc = trace.StartProcess("SN_OK", "P1", "S1", "D1");
        trace.EndProcess(okProc.Id, true, "OK");

        trace.StartTrace("SN_NG", "LOT1", "P");
        var ngProc = trace.StartProcess("SN_NG", "P1", "S1", "D1");
        trace.EndProcess(ngProc.Id, false, "NG");

        var from = DateTime.Now.AddMinutes(-1);
        var to = DateTime.Now.AddMinutes(1);

        var oks = trace.Query(from, to, true);
        var ngs = trace.Query(from, to, false);

        Assert.Contains(oks, x => x.SN == "SN_OK");
        Assert.Contains(ngs, x => x.SN == "SN_NG");
    }
}
