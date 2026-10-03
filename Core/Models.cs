using System;
using System.Collections.Generic;

namespace MAV.ProductionRegressionController
{
    public enum MavPackageType { Unknown = 0, Room = 1, VtcFarm = 2 }
    public enum RunMode { HealthCheck = 0, FullRegression = 1 }
    public enum RunStatus { Idle = 0, Uploading = 1, Ready = 2, Deploying = 3, WaitingForCws = 4, Testing = 5, Passed = 6, Warning = 7, Failed = 8, Aborted = 9 }

    public sealed class TargetSettings
    {
        public string Address { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public int ProgramSlot { get; set; }
        public bool UseHttpsCws { get; set; }
        public bool AllowUntrustedHttps { get; set; }
        public bool AllowReplaceWithoutRecoverableCpz { get; set; }
        public RunMode RunMode { get; set; }
        public MavPackageType TestProfile { get; set; }
    }

    public sealed class PackageInfo
    {
        public string CpzPath { get; set; }
        public string ConfigPath { get; set; }
        public string CpzFileName { get; set; }
        public string ConfigFileName { get; set; }
        public string CpzSha256 { get; set; }
        public string ConfigSha256 { get; set; }
        public MavPackageType PackageType { get; set; }
        public string PackageTypeName { get { return PackageType.ToString(); } }
        public string SystemName { get; set; }
        public string DetectionReason { get; set; }
    }

    public sealed class RegressionCheck
    {
        public int Sequence { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
        public DateTime TimestampUtc { get; set; }
        public TargetLogStepEvidence TargetEvidence { get; set; }
    }

    public sealed class RunState
    {
        public string SessionId { get; set; }
        public RunStatus Status { get; set; }
        public string Phase { get; set; }
        public string Detail { get; set; }
        public int Progress { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }
        public PackageInfo Package { get; set; }
        public TargetSettings Target { get; set; }
        public List<RegressionCheck> Checks { get; set; }
        public bool AbortRequested { get; set; }
        public bool SelfTarget { get; set; }
        public uint ControllerSlot { get; set; }
        public string EvidenceFolder { get; set; }

        public RunState()
        {
            Checks = new List<RegressionCheck>();
            Status = RunStatus.Idle;
            Phase = "Idle";
        }
    }

    public sealed class UploadStartRequest { public string Kind { get; set; } public string FileName { get; set; } public long Size { get; set; } }
    public sealed class UploadChunkRequest { public string Kind { get; set; } public int Index { get; set; } public string Base64 { get; set; } }
    public sealed class SessionConfigureRequest
    {
        public string Address { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public int ProgramSlot { get; set; }
        public bool UseHttpsCws { get; set; }
        public bool AllowUntrustedHttps { get; set; }
        public bool AllowReplaceWithoutRecoverableCpz { get; set; }
        public string RunMode { get; set; }
        public string TestProfile { get; set; }
    }

    public sealed class ApiEnvelope
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public object Value { get; set; }
        public static ApiEnvelope Ok(object value, string message) { return new ApiEnvelope { Success = true, Value = value, Message = message }; }
        public static ApiEnvelope Fail(string message) { return new ApiEnvelope { Success = false, Message = message }; }
    }
}
