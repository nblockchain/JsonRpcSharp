@ECHO OFF

SET COMMUNITY="%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Community\Common7\IDE\CommonExtensions\Microsoft\FSharp\fsi.exe"
SET ENTERPRISE="%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\CommonExtensions\Microsoft\FSharp\fsi.exe"
SET FSXSCRIPT1=scripts\cloneFsx.fsx
SET FSXSCRIPT2=scripts\nuget.fsx

IF EXIST %ENTERPRISE% (
    SET RUNNER=%ENTERPRISE%
) ELSE (
    IF EXIST %COMMUNITY% (
        SET RUNNER=%COMMUNITY%
    ) ELSE (
        ECHO fsi.exe not found, is F# installed?
        EXIT /b 1
    )
)

%RUNNER% %FSXSCRIPT1% %*
%RUNNER% %FSXSCRIPT2% %*
