
#Region " Imports "

Imports System.Drawing.Printing
Imports System.Runtime.InteropServices      'Printer Information
Imports System.Xml

#End Region

Public Class frmMain

#Region " Constants "

    Private Const vbLogEventTypeError As Int16 = 1
    Private Const vbLogEventTypeWarning As Int16 = 2
    Private Const vbLogEventTypeInformation As Int16 = 4

    Private Const WM_WININICHANGE = &H1A
    Private Const HWND_BROADCAST As Int32 = &HFFFF&

#End Region

#Region " Variables "

    Private oEvents As New Collection

    Private bDebugMode As Boolean
    Private bReportErrors As Boolean
    Private bReportWarnings As Boolean
    Private bReportInformation As Boolean
    Private bReportToEventLog As Boolean

    Private bWriteDebugMessagesToEventLog As Boolean

    Private strAppStatus As String
    Private strApp_Path As String

#End Region

#Region " Structures "

    Public Structure vbPrinter
        Dim OldServer As String
        Dim OldQueue As String
        Dim NewServer As String
        Dim NewQueue As String
        Dim bDefault As Boolean
    End Structure

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Auto)> Public Structure PRINTER_INFO_2
        Dim pServerName As String
        Dim pPrinterName As String
        Dim pShareName As String
        Dim pPortName As String
        Dim pDriverName As String
        Dim pComment As String
        Dim pLocation As String
        Dim pDevMode As Integer
        Dim pSepFile As String
        Dim pPrintProcessor As String
        Dim pDatatype As String
        Dim pParameters As String
        Dim pSecurityDescriptor As Integer
        Dim Attributes As Integer
        Dim Priority As Integer
        Dim DefaultPriority As Integer
        Dim StartTime As Integer
        Dim UntilTime As Integer
        Dim Status As Integer
        Dim cJobs As Integer
        Dim AveragePPM As Integer
    End Structure

#End Region

#Region " Enum's "

    Public Enum EventLogError
        ELError = vbLogEventTypeError             ' 1
        ELWarning = vbLogEventTypeWarning         ' 2
        ELInformation = vbLogEventTypeInformation ' 4
        ELDebugInfo = 8
    End Enum

    Public Enum ApplicationStatus
        statusUnknown = 0
        StatusError = 1
        statusWarning = 2
        statusOK = 4
    End Enum

#End Region

#Region " API Functions "

    Private Declare Function GetProfileString Lib "kernel32" Alias "GetProfileStringA" (ByVal lpAppName As String, ByVal lpKeyName As String, ByVal lpDefault As String, ByVal lpReturnedString As String, ByVal nSize As IntPtr) As IntPtr
    Private Declare Function WriteProfileString Lib "kernel32" Alias "WriteProfileStringA" (ByVal lpszSection As String, ByVal lpszKeyName As String, ByVal lpszString As String) As IntPtr
    Private Declare Function SendMessage Lib "user32" Alias "SendMessageA" (ByVal hwnd As IntPtr, ByVal wMsg As IntPtr, ByVal wParam As IntPtr, ByVal lparam As String) As IntPtr
    Private Declare Function AddPrinterConnection Lib "winspool.drv" Alias "AddPrinterConnectionA" (ByVal pName As String) As IntPtr
    Private Declare Function DeletePrinterConnection Lib "winspool.drv" Alias "DeletePrinterConnectionA" (ByVal pName As String) As IntPtr

    <DllImport("winspool.drv", EntryPoint:="OpenPrinterW", CharSet:=CharSet.Auto, SetLastError:=True, ExactSpelling:=True, CallingConvention:=CallingConvention.StdCall)> _
    Private Shared Function OpenPrinter(ByVal pPrinterName As String, ByRef hPrinter As IntPtr, ByVal pDefault As IntPtr) As Boolean
    End Function

    <DllImport("winspool.drv", CharSet:=CharSet.Auto, SetLastError:=True, ExactSpelling:=True, CallingConvention:=CallingConvention.StdCall)> _
    Private Shared Function ClosePrinter(ByVal hPrinter As IntPtr) As Boolean
    End Function

    <DllImport("winspool.drv", EntryPoint:="GetPrinterW", CharSet:=CharSet.Auto, SetLastError:=True, ExactSpelling:=True, CallingConvention:=CallingConvention.StdCall)> _
    Private Shared Function GetPrinter(ByVal hPrinter As IntPtr, ByVal dwLevel As Integer, ByVal pPrinter As IntPtr, ByVal cbBuf As Integer, ByRef pcbNeeded As Integer) As Boolean
    End Function

#End Region

#Region " GUI "

    Private Sub frmMain_Load(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles MyBase.Load
        ' Generate an App_Path variable that is the path to this application, similar to App.Path, though it will always end with a backslash.
        If Microsoft.VisualBasic.Right(My.Application.Info.DirectoryPath, 1) <> "\" Then
            strApp_Path = My.Application.Info.DirectoryPath & "\"
        Else
            strApp_Path = My.Application.Info.DirectoryPath
        End If

        bReportErrors = True
        bReportWarnings = True
        bReportInformation = True
        bDebugMode = False

        ' Set default status
        SetStatus(ApplicationStatus.statusOK)

        Me.Show()
        Me.Refresh()

        RedirectPrinters()

        ExitApplication()
    End Sub

#End Region

#Region " Subroutines and Functions "

    '---------------------------------------------------------------------------------------
    ' Procedure : RedirectPrinters
    ' DateTime  : 12/03/2003 22:46
    ' Author    : Dave Robinson
    ' Purpose   : Printer redirection
    '             There are 5 cases to interpret.
    '
    '             These are presented in an XML file called substitutes.xml.
    '             The structure of this file is as follows
    '               <Substitutes>
    '                   <Printers>
    '                       <Printer>
    '                           <OldServer>server1</OldServer>
    '                           <OldQueue>printer1</OldQueue>
    '                           <NewServer>server2</NewServer>
    '                           <NewQueue>printer2</NewQueue>
    '                       </Printer>
    '                   </Printers>
    '               </Substitutes>

    '
    '  V    History            Author
    ' 1.0   Initial Version    Dave Robinson
    '---------------------------------------------------------------------------------------
    '
    Private Sub RedirectPrinters()
        Dim strThisPrintServername As String = ""
        Dim strThisPrintername As String = ""                                       ' Current
        Dim strReplacementServername As String, strReplacementPrintername As String ' Replacement as specified
        Dim intPrinterCount As Integer = 0
        Dim strDefaultPrinterServername As String = ""                              ' Current DEFAULTS
        Dim strDefaultPrintername As String = ""                                    ' Current DEFAULTS
        Dim strNewDefaultPrintServer As String, strNewDefaultPrinter As String      ' New defaults
        Dim strNewPrintServername As String, strNewPrintername As String            ' Final that should be mapped
        Dim oPrinterList() As vbPrinter = Nothing
        Dim oCurrentPrinterList() As vbPrinter = Nothing
        Dim lPrinterNo As Long
        Dim bReplacePrinter As Boolean
        Dim bRemovePrinter As Boolean
        Dim intCurrentPrinters As Integer
        Dim intNewPrinters As Integer
        Dim bSetDefault As Boolean

        Dim strPrinterName As String = ""
        Dim prtPrinter As PrinterSettings
        Dim oExtraPrinterSettings As PRINTER_INFO_2
        Dim strServerName As String = ""
        Dim strShareName As String = ""

        On Error GoTo 0 ' TaskError

        LogEvent("Inside RedirectPrinters.  Printer Redirection.", EventLogError.ELInformation)

        OpenPrinterList(oPrinterList)
        LogEvent(UBound(oPrinterList) & " case(s) found in substitute list", EventLogError.ELDebugInfo)

        ' Create list of currently installed printers
        intPrinterCount = PrinterSettings.InstalledPrinters.Count

        Console.WriteLine(intPrinterCount & " installed printers.")

        For Each strPrinterName In PrinterSettings.InstalledPrinters
            prtPrinter = New PrinterSettings
            prtPrinter.PrinterName = strPrinterName
            oExtraPrinterSettings = GetPrinterInfo(strPrinterName)
            strServerName = LCase(GetServerFromPath(strPrinterName))
            strShareName = LCase(GetPathfromUNC(strPrinterName))

            If strServerName = "" Then ' Local printer
                strThisPrintServername = ""
                strThisPrintername = strPrinterName
                If prtPrinter.IsDefaultPrinter Then
                    strDefaultPrinterServername = ""
                    strDefaultPrintername = strThisPrintername
                    Console.WriteLine("[DEFAULT] " & strThisPrintername & " is local.")
                Else
                    Console.WriteLine(strPrinterName & " is local.")
                End If
            Else ' Network printer
                strThisPrintServername = strServerName
                strThisPrintername = strShareName
                If prtPrinter.IsDefaultPrinter Then
                    strDefaultPrinterServername = strThisPrintServername
                    strDefaultPrintername = strThisPrintername
                    Console.WriteLine("[DEFAULT] " & strThisPrintername & "'s Server is " & strServerName & ".  The Sharename is " & strShareName)
                Else
                    Console.WriteLine(strPrinterName & "'s Server is " & strServerName & ".  The Sharename is " & strShareName)
                End If
            End If

            ' Add 1 to the current printer 'ID'
            intCurrentPrinters += 1

            ' Redimension array to hold this printer info
            ReDim Preserve oCurrentPrinterList(intCurrentPrinters)

            ' Save the printer server and name
            oCurrentPrinterList(intCurrentPrinters).OldServer = strThisPrintServername
            oCurrentPrinterList(intCurrentPrinters).OldQueue = strThisPrintername

            If prtPrinter.IsDefaultPrinter Then
                oCurrentPrinterList(intCurrentPrinters).bDefault = True
            End If
        Next

        LogEvent("Printers defined on this system are as follows", EventLogError.ELDebugInfo)
        LogEvent("---------------Start Printer list---------------", EventLogError.ELDebugInfo)
        For intPrinterCount = 1 To intCurrentPrinters
            strThisPrintServername = oCurrentPrinterList(intPrinterCount).OldServer
            strThisPrintername = oCurrentPrinterList(intPrinterCount).OldQueue

            If strThisPrintServername = "" Then
                LogEvent("Currently installed printer: " & strThisPrintername, EventLogError.ELInformation)
            Else
                LogEvent("Currently installed printer: " & strThisPrintServername & "\" & strThisPrintername, EventLogError.ELInformation)
            End If

            If UBound(oPrinterList) > 0 Then
                For lPrinterNo = 1 To UBound(oPrinterList)
                    If strThisPrintServername = LCase(oPrinterList(lPrinterNo).OldServer) Then
                        ' Only a few cases
                        'case 1 - replace this printer with the nominated printername on the new server
                        If strThisPrintername = LCase(oPrinterList(lPrinterNo).OldQueue) Then
                            ' Check if we are removing the default printer
                            If oCurrentPrinterList(intCurrentPrinters).bDefault Then
                                LogEvent("This is the default printer.", EventLogError.ELInformation)
                                bSetDefault = True
                            End If
                            LogEvent("Removed.", EventLogError.ELInformation)
                            DeletePrinterConnection("\\" & oPrinterList(lPrinterNo).OldServer & "\" & oPrinterList(lPrinterNo).OldQueue & Chr(0))

                            LogEvent("Added " & oPrinterList(lPrinterNo).NewServer & "\" & oPrinterList(lPrinterNo).NewQueue & ".", EventLogError.ELInformation)
                            AddPrinterConnection("\\" & oPrinterList(lPrinterNo).NewServer & "\" & oPrinterList(lPrinterNo).NewQueue)
                            If bSetDefault Then
                                LogEvent("This is now the default printer.", EventLogError.ELInformation)
                                NTSetDefaultPrinter("\\" & oPrinterList(lPrinterNo).NewServer & "\" & oPrinterList(lPrinterNo).NewQueue)
                                bSetDefault = False
                            End If
                        End If

                        'case 2 - replace any printer on this server with the same printername on a new server
                        If strThisPrintServername = LCase(oPrinterList(lPrinterNo).OldServer) And oPrinterList(lPrinterNo).OldQueue = "*" And oPrinterList(lPrinterNo).NewQueue = "*" Then
                            ' Check if we are removing the default printer
                            If oCurrentPrinterList(intCurrentPrinters).bDefault Then
                                LogEvent("This is the default printer.", EventLogError.ELInformation)
                                bSetDefault = True
                            End If

                            LogEvent("Removed.", EventLogError.ELInformation)
                            DeletePrinterConnection("\\" & oPrinterList(lPrinterNo).OldServer & "\" & strThisPrintername & Chr(0))
                            LogEvent("Added " & oPrinterList(lPrinterNo).NewServer & "\" & strThisPrintername & ".", EventLogError.ELInformation)
                            AddPrinterConnection("\\" & oPrinterList(lPrinterNo).NewServer & "\" & strThisPrintername)
                            If bSetDefault Then
                                LogEvent("This is now the default printer.", EventLogError.ELInformation)
                                NTSetDefaultPrinter("\\" & oPrinterList(lPrinterNo).NewServer & "\" & oPrinterList(lPrinterNo).OldQueue)
                                bSetDefault = False
                            End If
                        End If

                        'case 3 - replace any printer on this server with the specified printername on a new server
                        If strThisPrintServername = LCase(oPrinterList(lPrinterNo).OldServer) And oPrinterList(lPrinterNo).OldQueue = "*" And oPrinterList(lPrinterNo).NewQueue <> "*" And oPrinterList(lPrinterNo).NewQueue <> "" Then
                            ' Check if we are removing the default printer
                            If oCurrentPrinterList(intCurrentPrinters).bDefault Then
                                LogEvent("This is the default printer.", EventLogError.ELInformation)
                                bSetDefault = True
                            End If
                            LogEvent("Removed.", EventLogError.ELInformation)
                            DeletePrinterConnection("\\" & oPrinterList(lPrinterNo).OldServer & "\" & strThisPrintername & Chr(0))
                            LogEvent("Added " & oPrinterList(lPrinterNo).NewServer & "\" & oPrinterList(lPrinterNo).NewQueue & ".", EventLogError.ELInformation)
                            AddPrinterConnection("\\" & oPrinterList(lPrinterNo).NewServer & "\" & oPrinterList(lPrinterNo).NewQueue)
                            If bSetDefault Then
                                LogEvent("This is now the default printer.", EventLogError.ELInformation)
                                NTSetDefaultPrinter("\\" & oPrinterList(lPrinterNo).NewServer & "\" & oPrinterList(lPrinterNo).NewQueue)
                                bSetDefault = False
                            End If
                        End If

                        'case 4 - remove this printer
                        If strThisPrintServername = LCase(oPrinterList(lPrinterNo).OldServer) And oPrinterList(lPrinterNo).NewQueue = "" Then
                            ' Check if we are removing the default printer
                            If oCurrentPrinterList(intCurrentPrinters).bDefault Then
                                LogEvent("This was the default printer, and will not be replaced.", EventLogError.ELInformation)
                            End If
                            LogEvent("Removed.", EventLogError.ELInformation)
                            DeletePrinterConnection("\\" & oPrinterList(lPrinterNo).OldServer & "\" & oPrinterList(lPrinterNo).OldQueue & Chr(0))
                        End If

                        ' case 5 - remove any printer on this server
                        If strThisPrintServername = LCase(oPrinterList(lPrinterNo).OldServer) And oPrinterList(lPrinterNo).OldQueue = "*" And oPrinterList(lPrinterNo).NewQueue = "" Then
                            ' Check if we are removing the default printer
                            If oCurrentPrinterList(intCurrentPrinters).bDefault Then
                                LogEvent("This was the default printer, and will not be replaced.", EventLogError.ELInformation)
                            End If
                            LogEvent("Removed.", EventLogError.ELInformation)
                            DeletePrinterConnection("\\" & oPrinterList(lPrinterNo).OldServer & "\" & strThisPrintername & Chr(0))
                        End If
                    End If
                Next
            End If
        Next
        LogEvent("----------------End Printer list----------------", EventLogError.ELDebugInfo)

        If intPrinterCount > 0 Then
            LogEvent("Default Printer info- Server: " & strDefaultPrinterServername & " Name: " & strDefaultPrintername)
        Else
            LogEvent("There are no printers defined on this system.", EventLogError.ELInformation)
        End If

        Exit Sub
TaskError:
        Select Case Err.Number
            Case 9
                ' This is expected if there are no printers listed in substitutes.xml
                Err.Clear()
            Case Else
                LogEvent("**** Logon encountered internal error " & Err.Number & " (" & Err.Description & ") in RedirectPrinters.", EventLogError.ELError)
        End Select
        Err.Clear()
        Resume Next
    End Sub

    ' --------------------------------------------------------------------------------------
    ' ---------------------------------- Functions -----------------------------------------

    '---------------------------------------------------------------------------------------
    ' Procedure : LogEvent
    ' DateTime  : 14-03-2003 10:34
    ' Author    : Dave Robinson
    ' Purpose   : Keep track of application events
    '
    '  V    History            Author
    ' 1.0   Initial Version    Dave Robinson
    '---------------------------------------------------------------------------------------
    '
    Private Sub LogEvent(ByVal strMessage As String, Optional ByVal EventType As EventLogError = EventLogError.ELInformation)
        Dim bDoReport As Boolean

        Me.lstStatus.Items.Add(strMessage)

        On Error GoTo LogEvent_Error

        If bReportErrors And EventType = EventLogError.ELError Then
            bDoReport = True
        End If

        If bReportWarnings And EventType = EventLogError.ELWarning Then
            bDoReport = True
        End If

        If bReportInformation And EventType = EventLogError.ELInformation Then
            bDoReport = True
        End If

        If bDebugMode And EventType = EventLogError.ELDebugInfo Then
            bDoReport = True
        End If

        ' Only add this event if we have determined it should be (according to the config options)
        If bDoReport Then
            oEvents.Add(EventType & "|" & strMessage)
            If (EventType <> EventLogError.ELInformation) And EventType <> EventLogError.ELDebugInfo Then
                'App.LogEvent(strMessage, EventType)
                lblStatus.Text = strMessage
                'Me.lstStatus.Items.Add(strMessage)
                SetStatus(EventType)
            ElseIf EventType = EventLogError.ELInformation Then
                lblStatus.Text = strMessage
                'Me.lstStatus.Items.Add(strMessage)
            ElseIf EventType = EventLogError.ELDebugInfo Then
                If bWriteDebugMessagesToEventLog Then
                    'App.LogEvent(strMessage, vbLogEventTypeInformation)
                    lblStatus.Text = strMessage
                    'Me.lstStatus.Items.Add(strMessage)
                End If
                SetStatus(ApplicationStatus.statusUnknown)
            End If
        End If

        Me.Refresh()

        On Error GoTo 0
        Exit Sub

LogEvent_Error:

        LogEvent("**** Error " & Err.Number & " (" & Err.Description & ") in procedure LogEvent", EventLogError.ELError)
    End Sub

    '---------------------------------------------------------------------------------------
    ' Procedure : SetStatus
    ' DateTime  : 14-03-2003 10:38
    ' Author    : Dave Robinson
    ' Purpose   : Set the application status
    '
    '  V    History            Author
    ' 1.0   Initial Version    Dave Robinson
    '---------------------------------------------------------------------------------------
    '
    Private Sub SetStatus(ByVal Status As ApplicationStatus)
        On Error GoTo SetStatus_Error

        Select Case Status
            Case ApplicationStatus.statusUnknown
                strAppStatus = "Unknown or Debug Mode"
            Case ApplicationStatus.statusOK
                strAppStatus = "OK"
            Case ApplicationStatus.StatusError
                strAppStatus = "Error"
            Case ApplicationStatus.statusWarning
                strAppStatus = "Warning"
        End Select

        On Error GoTo 0
        Exit Sub

SetStatus_Error:

        LogEvent("**** Error " & Err.Number & " (" & Err.Description & ") in procedure SetStatus of Module modGlobal", EventLogError.ELError)
    End Sub

    '---------------------------------------------------------------------------------------
    ' Procedure : UpdateStatus
    ' DateTime  : 12/03/2003 22:45
    ' Author    : Dave Robinson
    ' Purpose   : Update Form with status
    '
    '  V    History            Author
    ' 1.0   Initial Version    Dave Robinson
    '---------------------------------------------------------------------------------------
    '
    Private Sub UpdateStatus(ByVal intStatus As Integer)
        On Error GoTo UpdateStatusError

        ' Update the Progressbar

        Exit Sub
UpdateStatusError:
        LogEvent("**** Logon encountered internal error " & Err.Number & " (" & Err.Description & ") in UpdateStatus.", EventLogError.ELError)
        Err.Clear()
    End Sub

    '---------------------------------------------------------------------------------------
    ' Procedure : OpenPrinterList
    ' DateTime  : 31/03/2008 11:25
    ' Author    : Dave Robinson
    ' Purpose   : Open the Substitutes.xml file, storing all info into the supplied Dictionary object
    '
    '  V    History            Author
    ' 1.0   Initial Version    Dave Robinson
    ' 2.0   .NET Native        Dave Robinson
    '---------------------------------------------------------------------------------------
    Private Sub OpenPrinterList(ByRef PrinterList() As vbPrinter)
        Dim oXMLDocument As Xml.XmlDocument
        Dim oXMLNodelist As Xml.XmlNodeList
        Dim oXMLNode As Xml.XmlNode
        Dim oXMLChildNode1 As Xml.XmlNode
        Dim oXMLChildNode2 As Xml.XmlNode
        Dim strServerRole As String
        Dim lPrinterNo As Int32

        On Error GoTo OpenConfigError

        LogEvent("Inside OpenPrinterList subroutine", EventLogError.ELDebugInfo)
        ' Clear all items from the array before we start adding more (potentially duplicate) items to it.
        Erase PrinterList

        ' Create an XML Document to load the XML file into
        oXMLDocument = New Xml.XmlDocument

        ' Check to ensure the config.xml file exists
        If Dir(strApp_Path & "substitutes.xml") = "" Then
            LogEvent("Logon could not find the config file 'substitutes.xml' under the current path (" & strApp_Path & ").", EventLogError.ELWarning)
            ExitApplication()
        Else
            ' Load the XML file
            oXMLDocument.Load(strApp_Path & "substitutes.xml")
            ' Reference the nodes
            oXMLNodelist = oXMLDocument.GetElementsByTagName("Printers")

            ' #######################################################
            For Each oXMLNode In oXMLNodelist                             ' Printers
                For Each oXMLChildNode1 In oXMLNode.ChildNodes            ' Printer
                    lPrinterNo = lPrinterNo + 1
                    ReDim Preserve PrinterList(lPrinterNo)
                    For Each oXMLChildNode2 In oXMLChildNode1.ChildNodes
                        If oXMLChildNode2.Name <> "" Then
                            Select Case LCase(oXMLChildNode2.Name)
                                Case "oldserver"
                                    PrinterList(lPrinterNo).OldServer = oXMLChildNode2.InnerText
                                    LogEvent("Old Server: " & PrinterList(lPrinterNo).OldServer)
                                Case "oldqueue"
                                    PrinterList(lPrinterNo).OldQueue = oXMLChildNode2.InnerText
                                    LogEvent("Old Queue: " & PrinterList(lPrinterNo).OldQueue)
                                Case "newserver"
                                    PrinterList(lPrinterNo).NewServer = oXMLChildNode2.InnerText
                                    LogEvent("New Server: " & PrinterList(lPrinterNo).NewServer)
                                Case "newqueue"
                                    PrinterList(lPrinterNo).NewQueue = oXMLChildNode2.InnerText
                                    LogEvent("New Queue: " & PrinterList(lPrinterNo).NewQueue)
                            End Select
                        End If
                    Next
                Next
            Next
        End If

        ' Clean up
        oXMLDocument = Nothing

        Exit Sub

OpenConfigError:
        LogEvent("**** Logon encountered internal error " & Err.Number & " (" & Err.Description & ") in OpenPrinterList.", EventLogError.ELError)

        oXMLDocument = Nothing
        Err.Clear()
    End Sub

    '---------------------------------------------------------------------------------------
    ' Procedure : NTSetDefaultPrinter
    ' DateTime  : 31-03-2003 13:57
    ' Author    : Dave Robinson
    ' Purpose   : Sets the default printer under Windows NT/2000/XP
    '
    '  V    Date    Author      History
    ' 1.0   31-03-2003  Dave Robinson   Initial Version
    '---------------------------------------------------------------------------------------
    Private Sub NTSetDefaultPrinter(ByVal strPrinter As String)
        Dim Buffer As String = ""
        Dim DeviceName As String
        Dim DriverName As String = ""
        Dim PrinterPort As String = ""
        Dim PrinterName As String = ""
        Dim r As Long = 0

        On Error GoTo NTSetDefaultPrinter_Error

        'Get the printer information for the currently selected printer in the list. The information is taken from the WIN.INI file.
        Buffer = Space(1024)
        PrinterName = strPrinter
        r = GetProfileString("PrinterPorts", PrinterName, "", Buffer, Len(Buffer))

        'Parse the driver name and port name out of the buffer
        GetDriverAndPort(Buffer, DriverName, PrinterPort)

        If DriverName <> "" And PrinterPort <> "" Then
            LogEvent("Setting default printer to " & PrinterName, EventLogError.ELInformation)
            SetDefaultPrinter(PrinterName, DriverName, PrinterPort)
        End If

        On Error GoTo 0
        Exit Sub

NTSetDefaultPrinter_Error:

        LogEvent("Error " & Err.Number & " (" & Err.Description & ") in procedure NTSetDefaultPrinter", EventLogError.ELError)
    End Sub

    '---------------------------------------------------------------------------------------
    ' Procedure : SetDefaultPrinter
    ' DateTime  : 01-04-2003 15:25
    ' Author    : Dave Robinson
    ' Purpose   : Sets the default WINDOWS Printer
    '
    '  V    Date    Author      History
    ' 1.0   01-04-2003  Dave Robinson   Initial Version
    '---------------------------------------------------------------------------------------
    Private Sub SetDefaultPrinter(ByVal PrinterName As String, ByVal DriverName As String, ByVal PrinterPort As String)
        Dim DeviceLine As String
        Dim r As Long
        Dim l As Long
        On Error GoTo SetDefaultPrinter_Error

        DeviceLine = PrinterName & "," & DriverName & "," & PrinterPort
        ' Store the new printer information in the [WINDOWS] section of
        ' the WIN.INI file for the DEVICE= item
        r = WriteProfileString("windows", "Device", DeviceLine)
        ' Cause all applications to reload the INI file:
        l = SendMessage(HWND_BROADCAST, WM_WININICHANGE, 0, "windows")

        On Error GoTo 0
        Exit Sub

SetDefaultPrinter_Error:

        LogEvent("Error " & Err.Number & " (" & Err.Description & ") in procedure SetDefaultPrinter", EventLogError.ELError)
    End Sub

    '---------------------------------------------------------------------------------------
    ' Procedure : GetDriverAndPort
    ' DateTime  : 31-03-2003 13:56
    ' Author    : Dave Robinson
    ' Purpose   : Return the
    '
    '  V    Date        Author          History
    ' 1.0   31-03-2003  Dave Robinson   Initial Version
    '---------------------------------------------------------------------------------------
    Private Sub GetDriverAndPort(ByVal Buffer As String, ByVal DriverName As String, ByVal PrinterPort As String)
        Dim iDriver As Integer
        Dim iPort As Integer
        On Error GoTo GetDriverAndPort_Error

        DriverName = ""
        PrinterPort = ""

        'The driver name is first in the string terminated by a comma
        iDriver = InStr(Buffer, ",")
        If iDriver > 0 Then

            'Strip out the driver name
            DriverName = Microsoft.VisualBasic.Left(Buffer, iDriver - 1)

            'The port name is the second entry after the driver name
            'separated by commas.
            iPort = InStr(iDriver + 1, Buffer, ",")

            If iPort > 0 Then
                'Strip out the port name
                PrinterPort = Mid(Buffer, iDriver + 1, iPort - iDriver - 1)
            End If
        End If

        On Error GoTo 0
        Exit Sub

GetDriverAndPort_Error:

        LogEvent("Error " & Err.Number & " (" & Err.Description & ") in procedure GetDriverAndPort", EventLogError.ELError)
    End Sub

    '---------------------------------------------------------------------------------------
    ' Procedure : GetPathfromUNC
    ' DateTime  : 14-03-2003 10:29
    ' Author    : Dave Robinson
    ' Purpose   : Retrieve the path or share or printer (etc) portion of the given UNC
    '
    '  V    History            Author
    ' 1.0   Initial Version    Dave Robinson
    '---------------------------------------------------------------------------------------
    '
    Private Function GetPathfromUNC(ByVal strUNCPath As String) As String
        Dim intTemp1 As Integer
        Dim strPath As String
        On Error GoTo GetPathfromUNC_Error

        If InStr(1, strUNCPath, "\\") = 0 Then
            Return ""
            Exit Function
        End If
        intTemp1 = InStr(3, Trim(strUNCPath), "\")
        strPath = Microsoft.VisualBasic.Right(Trim(strUNCPath), Len(Trim(strUNCPath)) - intTemp1)
        GetPathfromUNC = UCase(strPath)

        On Error GoTo 0
        Exit Function

GetPathfromUNC_Error:

        LogEvent("**** Error " & Err.Number & " (" & Err.Description & ") in procedure GetPathfromUNC", EventLogError.ELError)
    End Function

    '---------------------------------------------------------------------------------------
    ' Procedure : GetServerFromPath
    ' DateTime  : 14-03-2003 10:29
    ' Author    : Dave Robinson
    ' Purpose   : Retrieve the server portion of the given UNC
    '
    '  V    History            Author
    ' 1.0   Initial Version    Dave Robinson
    '---------------------------------------------------------------------------------------
    '
    Private Function GetServerFromPath(ByVal strUNCPath As String) As String
        Dim intTemp1 As Integer
        Dim strPath As String
        On Error GoTo GetServerFromPath_Error

        If InStr(1, strUNCPath, "\\") = 0 Then
            Return ""
            Exit Function
        End If
        intTemp1 = InStr(3, Trim(strUNCPath), "\")
        strPath = Microsoft.VisualBasic.Left(Trim(strUNCPath), intTemp1 - 1)
        GetServerFromPath = UCase(Microsoft.VisualBasic.Right(strPath, Len(strPath) - 2))

        On Error GoTo 0
        Exit Function

GetServerFromPath_Error:

        LogEvent("**** Error " & Err.Number & " (" & Err.Description & ") in procedure GetServerFromPath", EventLogError.ELError)
    End Function

    '---------------------------------------------------------------------------------------
    ' Procedure : GetPrinterInfo
    ' DateTime  : 31-03-2008 11:56
    ' Author    : Dave Robinson
    ' Purpose   : Return Printer Info as a PRINTER_INFO_2 Object
    '
    '  V    Date        Author          History
    ' 1.0   31-03-2008  Dave Robinson   Initial Version
    '---------------------------------------------------------------------------------------
    Private Function GetPrinterInfo(ByVal sName As String) As PRINTER_INFO_2
        Dim hPrinter As IntPtr = IntPtr.Zero
        Dim pPrinterInfo As IntPtr = IntPtr.Zero
        Dim iNeed As Integer = -1
        Dim SizeOf As Integer = -1
        Try

            'Open printer object
            If (Not OpenPrinter(sName, hPrinter, IntPtr.Zero)) Then
                Marshal.ThrowExceptionForHR(System.Runtime.InteropServices.Marshal.GetHRForLastWin32Error())
            End If

            Try
                ' Get the number of bytes needed. 
                GetPrinter(hPrinter, 2, IntPtr.Zero, 0, iNeed)

                ' Allocate enough memory. 
                pPrinterInfo = Marshal.AllocHGlobal(iNeed)
                SizeOf = iNeed
                If (Not GetPrinter(hPrinter, 2, pPrinterInfo, SizeOf, iNeed)) Then
                    Marshal.ThrowExceptionForHR(System.Runtime.InteropServices.Marshal.GetHRForLastWin32Error())
                End If

                ' Now marshal the structure manually. 
                Dim PrinterInfo As PRINTER_INFO_2 = CType(Marshal.PtrToStructure(pPrinterInfo, GetType(PRINTER_INFO_2)), PRINTER_INFO_2)

                Return PrinterInfo
            Catch ex As Exception
                LogEvent("Error retrieving info for " & sName, EventLogError.ELInformation)
                LogEvent(ex.Message, EventLogError.ELError)
            Finally
                ' Close the printer object. 
                ClosePrinter(hPrinter)
                ' Deallocate the memory. 
                Marshal.FreeHGlobal(pPrinterInfo)
            End Try
        Catch ex As Exception
            LogEvent("Error retrieving info for " & sName, EventLogError.ELInformation)
            LogEvent(ex.Message, EventLogError.ELError)
        End Try
        Return New PRINTER_INFO_2()
    End Function

    Private Sub ExitApplication()
        Dim strOutput As String = ""
        Dim strOutputPath As String = My.Computer.FileSystem.SpecialDirectories.Temp.ToString & "\RedirectPrinters.log"
        Dim oFS As New System.IO.StreamWriter(strOutputPath)

        LogEvent("Exiting application.  Status = " & strAppStatus, EventLogError.ELInformation)
        For i As Integer = 1 To oEvents.Count
            strOutput &= oEvents(i).ToString & vbCrLf
        Next

        Console.WriteLine(strOutput)

        oFS.Write(strOutput)
        oFS.Close()

        End
    End Sub

#End Region

End Class

