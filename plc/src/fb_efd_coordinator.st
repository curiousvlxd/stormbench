FUNCTION_BLOCK FB_EfdCoordinator
  VAR_INPUT
    Mode : LINT;
    Depth : ARRAY[1..4] OF LREAL;
    BaseSetting : ARRAY[1..4] OF LREAL;
    LowSetting : ARRAY[1..4] OF LREAL;
    ThresholdDepth : LREAL;
  END_VAR
  VAR_OUTPUT
    Setpoint : ARRAY[1..4] OF LREAL;
  END_VAR
  VAR
    i : INT;
    j : INT;
    allBelow : BOOL;
    fullest : BOOL;
    emptiest : BOOL;
  END_VAR
  VAR CONSTANT
    MODE_EFD_INTENDED : LINT := 2;
  END_VAR

  allBelow := TRUE;
  FOR i := 1 TO 4 DO
    IF NOT (Depth[i] < ThresholdDepth) THEN
      allBelow := FALSE;
    END_IF;
  END_FOR;

  FOR i := 1 TO 4 DO
    fullest := TRUE;
    emptiest := TRUE;
    FOR j := 1 TO 4 DO
      IF j <> i THEN
        IF NOT (Depth[i] > Depth[j]) THEN
          fullest := FALSE;
        END_IF;
        IF NOT (Depth[i] < Depth[j]) THEN
          emptiest := FALSE;
        END_IF;
      END_IF;
    END_FOR;

    IF (Mode = MODE_EFD_INTENDED) AND allBelow THEN
      Setpoint[i] := BaseSetting[i];
    ELSIF fullest THEN
      Setpoint[i] := 1.0;
    ELSIF (Mode = MODE_EFD_INTENDED) AND emptiest THEN
      Setpoint[i] := LowSetting[i];
    ELSE
      Setpoint[i] := BaseSetting[i];
    END_IF;
  END_FOR;
END_FUNCTION_BLOCK
