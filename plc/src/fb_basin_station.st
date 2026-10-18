FUNCTION_BLOCK FB_BasinStation
  VAR_INPUT
    RemoteSetpoint : LREAL;
    RemoteEnabled : BOOL;
    LocalSetting : LREAL;
    MinSetting : LREAL := 0.0;
    MaxSetting : LREAL := 1.0;
  END_VAR
  VAR_OUTPUT
    Setpoint : LREAL;
    OnLocal : BOOL;
    Clamped : BOOL;
  END_VAR

  IF RemoteEnabled THEN
    Setpoint := LIMIT(MinSetting, RemoteSetpoint, MaxSetting);
    Clamped := Setpoint <> RemoteSetpoint;
    OnLocal := FALSE;
  ELSE
    Setpoint := LocalSetting;
    Clamped := FALSE;
    OnLocal := TRUE;
  END_IF;
END_FUNCTION_BLOCK
