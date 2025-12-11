@echo off

echo Building ECUFlasher.sln using dotnet (Debug)...
dotnet build ECUFlasher.sln --configuration Debug --verbosity minimal
exit /b %ERRORLEVEL%
