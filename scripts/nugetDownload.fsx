#!/usr/bin/env fsharpi

open System
open System.IO

#r "System.Configuration"
#load "../fsx/InfraLib/Misc.fs"
#load "../fsx/InfraLib/Process.fs"
#load "../fsx/InfraLib/Network.fs"
open FSX.Infrastructure

let nugetDownloadUri = Uri "https://dist.nuget.org/win-x86-commandline/v4.5.1/nuget.exe"
Network.DownloadFile nugetDownloadUri
