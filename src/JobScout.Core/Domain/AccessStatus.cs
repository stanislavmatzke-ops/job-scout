namespace JobScout.Core.Domain;

/// <summary>ADR-0017: Pending = signed in, waits for approval; Approved = full use; Suspended = can read, costs nothing.</summary>
public enum AccessStatus { Pending, Approved, Suspended }
