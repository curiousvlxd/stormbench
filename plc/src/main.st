PROGRAM main
  VAR
    SimTimeS AT %ML0 : LINT;
    CmdMode AT %ML1 : LINT;
    CenterOk AT %ML2 : LINT;
    DepthT1 AT %ML3 : LREAL;
    DepthT2 AT %ML4 : LREAL;
    DepthT3 AT %ML5 : LREAL;
    DepthT4 AT %ML6 : LREAL;
    DepthT5 AT %ML7 : LREAL;
    DepthT6 AT %ML8 : LREAL;
    StepReq AT %ML9 : LINT;
    CenterSetpointV2 AT %ML10 : LREAL;
    CenterSetpointV3 AT %ML11 : LREAL;
    CenterSetpointV4 AT %ML12 : LREAL;
    CenterSetpointV6 AT %ML13 : LREAL;
    CenterStep AT %ML14 : LINT;
    SetpointV2 AT %ML16 : LREAL;
    SetpointV3 AT %ML17 : LREAL;
    SetpointV4 AT %ML18 : LREAL;
    SetpointV6 AT %ML19 : LREAL;
    ModeActive AT %ML20 : LINT;
    Status AT %ML21 : LINT;
    ScanCount AT %ML22 : LINT;
    StepAck AT %ML23 : LINT;

    Coordinator : FB_EfdCoordinator;
    Stations : ARRAY[1..4] OF FB_BasinStation;
    depth : ARRAY[1..4] OF LREAL;
    proposal : ARRAY[1..4] OF LREAL;
    requested : ARRAY[1..4] OF LREAL;
    setpoint : ARRAY[1..4] OF LREAL;
    i : INT;
    plausible : BOOL;
    inputsValid : BOOL;
    centerMode : BOOL;
    centerReady : BOOL;
    proposalValid : BOOL;
    coordinated : BOOL;
    remoteEnabled : BOOL;
    anyClamped : BOOL;
    statusWord : LINT;
  END_VAR
  VAR CONSTANT
    MODE_LOCAL : LINT := 0;
    MODE_EFD_PUBLISHED : LINT := 1;
    MODE_EFD_INTENDED : LINT := 2;
    MODE_EFD_CENTER : LINT := 3;
    STATUS_FALLBACK : LINT := 1;
    STATUS_INPUT_FAULT : LINT := 2;
    STATUS_CLAMPED : LINT := 4;
    STATUS_BAD_PROPOSAL : LINT := 8;
    MAX_DEPTH : LREAL := 5.0;
    DEPTH_TOLERANCE : LREAL := 0.05;
    THRESHOLD_DEPTH : LREAL := 1.0;
    BASE_SETTING : ARRAY[1..4] OF LREAL := [0.2366, 0.6508, 0.3523, 0.4303];
    LOW_SETTING : ARRAY[1..4] OF LREAL := [0.1075, 0.3159, 0.1894, 0.1687];
  END_VAR

  ScanCount := ScanCount + 1;

  IF StepReq <> StepAck THEN
    centerMode := CmdMode = MODE_EFD_CENTER;
    centerReady := (CenterStep = StepReq) OR (CenterOk = 0);

    IF (NOT centerMode) OR centerReady THEN
      depth[1] := DepthT2;
      depth[2] := DepthT3;
      depth[3] := DepthT4;
      depth[4] := DepthT6;

      inputsValid := TRUE;
      FOR i := 1 TO 4 DO
        plausible := (depth[i] >= 0.0) AND (depth[i] <= MAX_DEPTH + DEPTH_TOLERANCE);
        IF NOT plausible THEN
          inputsValid := FALSE;
        END_IF;
      END_FOR;

      IF centerMode THEN
        proposal[1] := CenterSetpointV2;
        proposal[2] := CenterSetpointV3;
        proposal[3] := CenterSetpointV4;
        proposal[4] := CenterSetpointV6;
        proposalValid := TRUE;
        FOR i := 1 TO 4 DO
          plausible := (proposal[i] >= 0.0) AND (proposal[i] <= 1.0);
          IF NOT plausible THEN
            proposalValid := FALSE;
          END_IF;
          requested[i] := proposal[i];
        END_FOR;
      ELSE
        Coordinator(Mode := CmdMode, Depth := depth, BaseSetting := BASE_SETTING,
                    LowSetting := LOW_SETTING, ThresholdDepth := THRESHOLD_DEPTH);
        proposalValid := TRUE;
        FOR i := 1 TO 4 DO
          requested[i] := Coordinator.Setpoint[i];
        END_FOR;
      END_IF;

      coordinated := (CmdMode = MODE_EFD_PUBLISHED) OR (CmdMode = MODE_EFD_INTENDED) OR centerMode;
      remoteEnabled := coordinated AND (CenterOk <> 0) AND inputsValid AND proposalValid;

      anyClamped := FALSE;
      FOR i := 1 TO 4 DO
        Stations[i](RemoteSetpoint := requested[i], RemoteEnabled := remoteEnabled,
                    LocalSetting := BASE_SETTING[i]);
        setpoint[i] := Stations[i].Setpoint;
        IF Stations[i].Clamped THEN
          anyClamped := TRUE;
        END_IF;
      END_FOR;

      statusWord := 0;
      IF coordinated AND NOT remoteEnabled THEN
        statusWord := statusWord + STATUS_FALLBACK;
      END_IF;
      IF NOT inputsValid THEN
        statusWord := statusWord + STATUS_INPUT_FAULT;
      END_IF;
      IF anyClamped THEN
        statusWord := statusWord + STATUS_CLAMPED;
      END_IF;
      IF centerMode AND (CenterOk <> 0) AND NOT proposalValid THEN
        statusWord := statusWord + STATUS_BAD_PROPOSAL;
      END_IF;

      SetpointV2 := setpoint[1];
      SetpointV3 := setpoint[2];
      SetpointV4 := setpoint[3];
      SetpointV6 := setpoint[4];
      IF remoteEnabled THEN
        ModeActive := CmdMode;
      ELSE
        ModeActive := MODE_LOCAL;
      END_IF;
      Status := statusWord;
      StepAck := StepReq;
    END_IF;
  END_IF;
END_PROGRAM
