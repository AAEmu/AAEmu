# Configuration
$projectPath = "..\AAEmu.Login"
$executableDir = "..\AAEmu.Login\bin\x64\Debug\net9.0"
$workingDirectory = "..\.server_files\AAEmu.Login"
$buildConfiguration = "Debug"

# Initialize variables
$global:watchProcess = $null
$global:loginProcess = $null

function CreateSymbolicLinks {
    param(
        [string]$sourceDir,
        [string]$destinationDir
    )
    
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Creating symbolic links from $sourceDir to $destinationDir" -ForegroundColor Cyan
    
    # Ensure destination directory exists
    if (-not (Test-Path -Path $destinationDir)) {
        New-Item -Path $destinationDir -ItemType Directory -Force | Out-Null
    }
    
    # List of directories and files to link - adjusted for Login Server
    $itemsToLink = @(
        "Config.json",
        "Data"
    )
    
    foreach ($item in $itemsToLink) {
        $sourcePath = Join-Path -Path $sourceDir -ChildPath $item
        $targetPath = Join-Path -Path $destinationDir -ChildPath $item
        
        # Skip if source doesn't exist
        if (-not (Test-Path -Path $sourcePath)) {
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Warning: Source $sourcePath does not exist, skipping..." -ForegroundColor Yellow
            continue
        }
        
        # Remove existing file or directory at target path
        if (Test-Path -Path $targetPath) {
            try {
                # Check if it's already a symlink pointing to our source
                $existingItem = Get-Item $targetPath -Force -ErrorAction SilentlyContinue
                if ($existingItem.LinkType -eq "SymbolicLink" -and $existingItem.Target -eq $sourcePath) {
                    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Link for $item already exists and is correct" -ForegroundColor Green
                    continue
                }
                
                # Otherwise, remove it
                Remove-Item -Path $targetPath -Force -Recurse -ErrorAction Stop
                Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Removed existing item at $targetPath" -ForegroundColor Yellow
            } 
            catch {
                Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Error removing existing item: $_" -ForegroundColor Red
                continue
            }
        }
        
        # Create symbolic link
        try {
            New-Item -ItemType SymbolicLink -Path $targetPath -Target $sourcePath -Force | Out-Null
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Created symbolic link: $targetPath -> $sourcePath" -ForegroundColor Green
        }
        catch {
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Error creating symbolic link: $_" -ForegroundColor Red
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Attempting to copy instead..." -ForegroundColor Yellow
            
            try {
                if ((Get-Item $sourcePath) -is [System.IO.DirectoryInfo]) {
                    Copy-Item -Path $sourcePath -Destination $targetPath -Recurse -Force
                } else {
                    Copy-Item -Path $sourcePath -Destination $targetPath -Force
                }
                Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Copied $item as fallback" -ForegroundColor Yellow
            } catch {
                Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Error copying item: $_" -ForegroundColor Red
            }
        }
    }
    
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Symbolic link creation complete" -ForegroundColor Cyan
}

# Function to find the login process spawned by dotnet watch
function Find-LoginProcess {
    $processes = Get-Process -Name "AAEmu.Login" -ErrorAction SilentlyContinue
    if ($processes.Count -gt 0) {
        return $processes[0]
    }
    return $null
}

# Main function to run dotnet watch
function Start-DotnetWatch {
    try {
        # Create symbolic links
        CreateSymbolicLinks -sourceDir $workingDirectory -destinationDir $executableDir

        # Set up dotnet watch with hot reload enabled
        $env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = 1
        
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Starting dotnet watch with hot-reload for $projectPath..." -ForegroundColor Cyan
        
        # Get full paths
        $fullProjectPath = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, $projectPath))
        $fullExecDirPath = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, $executableDir))
        
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Using project path: $fullProjectPath" -ForegroundColor Cyan
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Using output directory: $fullExecDirPath" -ForegroundColor Cyan
        
        # Start dotnet watch with hot-reload enabled
        $arguments = @(
            "watch", 
            "--project", $fullProjectPath, 
            "run", 
            "--framework", "net9.0", 
            "--detailed"
        )
        
        # Start the process with full paths and capture output
        # Use NoNewWindow to run in the same console window
        $global:watchProcess = Start-Process -FilePath "dotnet" -ArgumentList $arguments -WorkingDirectory $fullExecDirPath -NoNewWindow -PassThru
        
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Dotnet watch started with PID: $($global:watchProcess.Id)" -ForegroundColor Green
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Hot-reload is enabled - code changes should apply without restart" -ForegroundColor Green
        
        # Wait for the login process to start
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Waiting for login process to start..." -ForegroundColor Cyan
        Start-Sleep -Seconds 10
        
        # Find the login process
        $global:loginProcess = Find-LoginProcess
        if ($global:loginProcess -ne $null) {
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Login process found with PID: $($global:loginProcess.Id)" -ForegroundColor Green
        } else {
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Warning: Login process not found after waiting" -ForegroundColor Yellow
            
            # Check if watch process is still running
            if ($global:watchProcess -ne $null -and $global:watchProcess.HasExited) {
                Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Error: Dotnet watch process exited prematurely with exit code $($global:watchProcess.ExitCode)" -ForegroundColor Red
                
                # Try running dotnet build directly to see errors
                Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Running dotnet build to check for errors..." -ForegroundColor Yellow
                $buildOutput = & dotnet build $fullProjectPath --framework net9.0 --verbose
                Write-Host $buildOutput -ForegroundColor Gray
            }
        }
    }
    catch {
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Error starting dotnet watch: $_" -ForegroundColor Red
    }
}

# Function to send Ctrl+C to a process
function Send-CtrlC {
    param (
        [Parameter(Mandatory=$true)]
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds = 5
    )

    if ($Process -eq $null -or $Process.HasExited) {
        return $true
    }

    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Sending Ctrl+C to process $($Process.Id)..." -ForegroundColor Yellow
    
    try {
        # Load Windows Forms assembly if not already loaded
        if (-not ("System.Windows.Forms.SendKeys" -as [type])) {
            Add-Type -AssemblyName System.Windows.Forms
        }
        
        # Set focus to the process window if it has one
        if ($Process.MainWindowHandle -ne 0) {
            $MethodDefinition = @"
[DllImport("user32.dll")]
public static extern bool SetForegroundWindow(IntPtr hWnd);
"@
            Add-Type -MemberDefinition $MethodDefinition -Name WindowUtils -Namespace Win32
            [Win32.WindowUtils]::SetForegroundWindow($Process.MainWindowHandle) | Out-Null
            Start-Sleep -Milliseconds 200  # Give time for window to get focus
            
            # Send Ctrl+C
            [System.Windows.Forms.SendKeys]::SendWait("^c")
            
            # Wait for process to exit
            $gracefulExit = $Process.WaitForExit($TimeoutSeconds * 1000)
            if ($gracefulExit) {
                Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Process $($Process.Id) shut down gracefully" -ForegroundColor Green
                return $true
            }
        }
    }
    catch {
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Error sending Ctrl+C: $_" -ForegroundColor Red
    }
    
    return $false
}

# Function to clean up processes when script ends
function Cleanup {
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Cleaning up processes..." -ForegroundColor Yellow
    
    # Try to gracefully terminate login process first
    $loginProcess = Find-LoginProcess
    if ($loginProcess -ne $null -and !$loginProcess.HasExited) {
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Attempting graceful shutdown of login process..." -ForegroundColor Yellow
        $gracefulExit = Send-CtrlC -Process $loginProcess -TimeoutSeconds 15
        Start-Sleep -Seconds 15
        
        # If graceful exit failed, force termination
        if (!$gracefulExit -and !$loginProcess.HasExited) {
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Graceful shutdown failed, forcing termination..." -ForegroundColor Yellow
            Stop-Process -Id $loginProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
    
    # Try to gracefully stop the dotnet watch process
    if ($global:watchProcess -ne $null -and !$global:watchProcess.HasExited) {
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Attempting graceful shutdown of dotnet watch process..." -ForegroundColor Yellow
        $gracefulExit = Send-CtrlC -Process $global:watchProcess -TimeoutSeconds 5
        
        # If graceful exit failed, force termination
        if (!$gracefulExit -and !$global:watchProcess.HasExited) {
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Graceful shutdown failed, forcing termination..." -ForegroundColor Yellow
            Stop-Process -Id $global:watchProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
    
    # Remove any background jobs
    Get-Job | Remove-Job -Force -ErrorAction SilentlyContinue
    
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Cleanup complete" -ForegroundColor Yellow
}

# Start the watcher
Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Starting AAEmu.Login watcher using dotnet watch with hot-reload..." -ForegroundColor Cyan
Start-DotnetWatch

# Keep the script running
try {
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Watcher is running. Press Ctrl+C to stop." -ForegroundColor Cyan
    
    # Monitor for login process terminations and update our reference
    while ($true) {
        Start-Sleep -Seconds 5
        
        # Check if our login process reference is still valid
        if ($global:loginProcess -ne $null -and $global:loginProcess.HasExited) {
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Login process exited, looking for new process..." -ForegroundColor Yellow
            $global:loginProcess = Find-LoginProcess
            if ($global:loginProcess -ne $null) {
                Write-Host "[$(Get-Date -Format 'HH:mm:ss')] New login process found with PID: $($global:loginProcess.Id)" -ForegroundColor Green
            }
        }
        
        # Check if dotnet watch has exited unexpectedly
        if ($global:watchProcess -ne $null -and $global:watchProcess.HasExited) {
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Dotnet watch process exited unexpectedly, restarting..." -ForegroundColor Red
            Start-DotnetWatch
        }
    }
}
catch {
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Error occurred: $_" -ForegroundColor Red
}
finally {
    # This block will run when Ctrl+C is pressed or when the script encounters an error
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Shutting down..." -ForegroundColor Yellow
    Cleanup
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Watcher stopped" -ForegroundColor Yellow
}