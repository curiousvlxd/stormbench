using System.Text.RegularExpressions;

namespace StormBench.Contracts.Tests;

public partial class RegisterMapTests
{
    [Theory]
    [InlineData("SimTimeS", RegisterMap.SimTimeS)]
    [InlineData("CmdMode", RegisterMap.CmdMode)]
    [InlineData("CenterOk", RegisterMap.CenterOk)]
    [InlineData("DepthT1", RegisterMap.DepthFirst)]
    [InlineData("DepthT6", RegisterMap.DepthFirst + 5)]
    [InlineData("StepReq", RegisterMap.StepReq)]
    [InlineData("CenterSetpointV2", RegisterMap.CenterSetpointFirst)]
    [InlineData("CenterSetpointV6", RegisterMap.CenterSetpointFirst + 3)]
    [InlineData("CenterStep", RegisterMap.CenterStep)]
    [InlineData("SetpointV2", RegisterMap.SetpointFirst)]
    [InlineData("SetpointV6", RegisterMap.SetpointFirst + 3)]
    [InlineData("ModeActive", RegisterMap.ModeActive)]
    [InlineData("Status", RegisterMap.Status)]
    [InlineData("ScanCount", RegisterMap.ScanCount)]
    [InlineData("StepAck", RegisterMap.StepAck)]
    public void Plc_program_declares_the_same_address(string variable, int mlIndex)
    {
        var st = RepositoryFiles.Read("plc", "src", "main.st");
        Assert.Contains($"{variable} AT %ML{mlIndex} :", st);
    }

    [Fact]
    public void Python_register_module_uses_the_same_indices()
    {
        var py = RepositoryFiles.Read("simulator", "stormbench_sim", "registers.py");
        Assert.Contains("HR_ML_BASE = 4096", py);
        Assert.Contains($"IN_DEPTH_FIRST = {RegisterMap.DepthFirst}", py);
        Assert.Contains($"CENTER_SETPOINT_FIRST = {RegisterMap.CenterSetpointFirst}", py);
        Assert.Contains($"OUT_FIRST = {RegisterMap.SetpointFirst}", py);
    }

    [Fact]
    public void Snapshot_fits_one_read_request() =>
        Assert.True(RegisterMap.SnapshotValues * RegisterMap.RegistersPerValue <= 125);

    [Fact]
    public void Doubles_round_trip_exactly_high_word_first()
    {
        var registers = new ushort[4];
        RegisterMap.WriteDouble(registers, 0, 1.0);
        Assert.Equal([0x3FF0, 0, 0, 0], registers);
        foreach (var value in new[] { 0.2366, 4.999999999, double.Epsilon, -2.5, Math.PI })
        {
            RegisterMap.WriteDouble(registers, 0, value);
            Assert.Equal(value, RegisterMap.ReadDouble(registers, 0));
        }
    }

    [Fact]
    public void Center_proposal_ends_with_the_step()
    {
        var registers = RegisterMap.EncodeCenterProposal(42, [1.0, 0.6508, 0.3523, 0.4303]);
        Assert.Equal(20, registers.Length);
        Assert.Equal(42, RegisterMap.ReadInt64(registers, 4));
        Assert.Equal(0.6508, RegisterMap.ReadDouble(registers, 1));
    }

    [Fact]
    public void Snapshot_decodes_every_field()
    {
        var registers = new ushort[RegisterMap.SnapshotValues * RegisterMap.RegistersPerValue];
        RegisterMap.WriteInt64(registers, RegisterMap.SimTimeS, 300);
        RegisterMap.WriteInt64(registers, RegisterMap.CmdMode, 3);
        RegisterMap.WriteInt64(registers, RegisterMap.CenterOk, 1);
        RegisterMap.WriteDouble(registers, RegisterMap.DepthFirst + 1, 2.0);
        RegisterMap.WriteInt64(registers, RegisterMap.StepReq, 7);
        RegisterMap.WriteDouble(registers, RegisterMap.SetpointFirst + 3, 1.0);
        RegisterMap.WriteInt64(registers, RegisterMap.StepAck, 6);

        var snapshot = PlcSnapshot.Decode(registers);

        Assert.Equal(300, snapshot.SimTimeS);
        Assert.Equal(ControlMode.EfdCenter, snapshot.CmdMode);
        Assert.True(snapshot.CenterOk);
        Assert.Equal(2.0, snapshot.Depths[1]);
        Assert.Equal(7, snapshot.StepReq);
        Assert.Equal(1.0, snapshot.Setpoints[3]);
        Assert.Equal(6, snapshot.StepAck);
    }

    [Fact]
    public void Control_modes_match_the_plc_constants()
    {
        var st = RepositoryFiles.Read("plc", "src", "main.st");
        foreach (var (name, mode) in new[]
                 {
                     ("MODE_LOCAL", ControlMode.Local), ("MODE_EFD_PUBLISHED", ControlMode.EfdPublished),
                     ("MODE_EFD_INTENDED", ControlMode.EfdIntended), ("MODE_EFD_CENTER", ControlMode.EfdCenter),
                 })
        {
            var match = ConstantRegex(name).Match(st);
            Assert.True(match.Success, name);
            Assert.Equal((long)mode, long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static Regex ConstantRegex(string name) => new($@"{name} : LINT := (\d+);");
}
