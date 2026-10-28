' Desktop Organizer launcher: first run copies the example settings, builds DesktopOrganizer.exe (again after any update), then starts it.
Set sh = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
d = fso.GetParentFolderName(WScript.ScriptFullName)
If Not fso.FileExists(d & "\settings.json") Then fso.CopyFile d & "\settings.example.json", d & "\settings.json"
If Not fso.FileExists(d & "\sources.json") Then fso.CopyFile d & "\sources.example.json", d & "\sources.json"
args = ""
For Each a In WScript.Arguments : args = args & " " & a : Next

' Starting PowerShell costs a second or two, so only build when a source file is newer than the exe.
needBuild = Not fso.FileExists(d & "\DesktopOrganizer.exe")
If Not needBuild Then
    built = fso.GetFile(d & "\DesktopOrganizer.exe").DateLastModified
    For Each f In fso.GetFolder(d & "\src").Files
        If LCase(fso.GetExtensionName(f.Name)) = "cs" And f.DateLastModified > built Then needBuild = True
    Next
End If

If needBuild Then
    rc = sh.Run("powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File """ & d & "\build.ps1""", 0, True)
    If rc <> 0 Or Not fso.FileExists(d & "\DesktopOrganizer.exe") Then
        MsgBox "Desktop Organizer couldn't be built. Details are in:" & vbCrLf & d & "\data\build.log", vbExclamation, "Desktop Organizer"
        WScript.Quit 1
    End If
End If
sh.Run """" & d & "\DesktopOrganizer.exe""" & args, 1, False
