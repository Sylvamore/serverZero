'
' Copyright (C) 2013-2021 getMaNGOS <https://getmangos.eu>
'
' This program is free software. You can redistribute it and/or modify
' it under the terms of the GNU General Public License as published by
' the Free Software Foundation. either version 2 of the License, or
' (at your option) any later version.
'
' This program is distributed in the hope that it will be useful,
' but WITHOUT ANY WARRANTY. Without even the implied warranty of
' MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
' GNU General Public License for more details.
'
' You should have received a copy of the GNU General Public License
' along with this program. If not, write to the Free Software
' Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
'

Imports System.IO
Imports System.Net
Imports System.Reflection
Imports System.Threading
Imports System.Xml.Serialization
Imports Mangos.SignalR
Imports Mangos.Common
Imports Mangos.Common.Globals
Imports Mangos.Common.Logging
Imports Mangos.Common.Logging.BaseWriter
Imports Mangos.Cluster.Globals
Imports Mangos.Cluster.Handlers
Imports Mangos.Cluster.Server
Imports Mangos.Common.Enums.Global
Imports Mangos.Configuration

Public Class WorldCluster
    Private Const ClusterPath As String = "configs/WorldCluster.ini"
    Private server As ProxyServer(Of WC_Network.WorldServerClass)

    'Players' containers
    Public CLIETNIDs As Long = 0

    Public CLIENTs As New Dictionary(Of UInteger, WC_Network.ClientClass)

    Public CHARACTERs_Lock As New ReaderWriterLock
    Public CHARACTERs As New Dictionary(Of ULong, WcHandlerCharacter.CharacterObject)
    'Public CHARACTER_NAMEs As New Hashtable

    'System Things...
    Public Log As New BaseWriter
    Public Rnd As New Random


    Delegate Sub HandlePacket(ByRef packet As Packets.PacketClass, ByRef client As WC_Network.ClientClass)

    Public Sub LoadConfig()
        Try
            'Make sure WorldCluster.ini exists
            If File.Exists(ClusterPath) = False Then
                Console.ForegroundColor = ConsoleColor.Red
                Console.WriteLine("[{0}] Cannot Continue. {1} does not exist.", Format(TimeOfDay, "hh:mm:ss"), ClusterPath)
                Console.WriteLine("Please make sure your ini files are inside config folder where the mangosvb executables are located.")
                Console.WriteLine("Press any key to exit server: ")
                Console.ReadKey()
                End
            End If

            Console.Write("[{0}] Loading Configuration...", Format(TimeOfDay, "hh:mm:ss"))

            Dim configuration = _configurationProvider.GetConfiguration()

            Console.WriteLine(".[done]")

            'DONE: Setting SQL Connections
            Dim AccountDBSettings() As String = Split(configuration.AccountDatabase, ";")
            If AccountDBSettings.Length <> 6 Then
                Console.WriteLine("Invalid connect string for the account database!")
            Else
                AccountDatabase.SQLDBName = AccountDBSettings(4)
                AccountDatabase.SQLHost = AccountDBSettings(2)
                AccountDatabase.SQLPort = AccountDBSettings(3)
                AccountDatabase.SQLUser = AccountDBSettings(0)
                AccountDatabase.SQLPass = AccountDBSettings(1)
                AccountDatabase.SQLTypeServer = [Enum].Parse(GetType(SQL.DB_Type), AccountDBSettings(5))
            End If

            Dim CharacterDBSettings() As String = Split(configuration.CharacterDatabase, ";")
            If CharacterDBSettings.Length <> 6 Then
                Console.WriteLine("Invalid connect string for the character database!")
            Else
                CharacterDatabase.SQLDBName = CharacterDBSettings(4)
                CharacterDatabase.SQLHost = CharacterDBSettings(2)
                CharacterDatabase.SQLPort = CharacterDBSettings(3)
                CharacterDatabase.SQLUser = CharacterDBSettings(0)
                CharacterDatabase.SQLPass = CharacterDBSettings(1)
                CharacterDatabase.SQLTypeServer = [Enum].Parse(GetType(SQL.DB_Type), CharacterDBSettings(5))
            End If

            Dim WorldDBSettings() As String = Split(configuration.WorldDatabase, ";")
            If WorldDBSettings.Length <> 6 Then
                Console.WriteLine("Invalid connect string for the world database!")
            Else
                WorldDatabase.SQLDBName = WorldDBSettings(4)
                WorldDatabase.SQLHost = WorldDBSettings(2)
                WorldDatabase.SQLPort = WorldDBSettings(3)
                WorldDatabase.SQLUser = WorldDBSettings(0)
                WorldDatabase.SQLPass = WorldDBSettings(1)
                WorldDatabase.SQLTypeServer = [Enum].Parse(GetType(SQL.DB_Type), WorldDBSettings(5))
            End If

            'DONE: Creating logger
            Log = CreateLog(configuration.LogType, configuration.LogConfig)
            Log.LogLevel = configuration.LogLevel

            'DONE: Cleaning up the packet log
            If configuration.PacketLogging Then
                File.Delete("packets.log")
            End If

        Catch e As Exception
            Console.WriteLine(e.ToString)
        End Try
    End Sub

#Region "WS.DataAccess"

    Public Property PacketHandlers As New Dictionary(Of OPCODES, HandlePacket)

    Public Property AccountDatabase As New SQL
    Public Property CharacterDatabase As New SQL
    Public Property WorldDatabase As New SQL

    Public Sub AccountSQLEventHandler(messageId As SQL.EMessages, outBuf As String)
        Select Case messageId
            Case SQL.EMessages.ID_Error
                Log.WriteLine(LogType.FAILED, "[ACCOUNT] " & outBuf)
            Case SQL.EMessages.ID_Message
                Log.WriteLine(LogType.SUCCESS, "[ACCOUNT] " & outBuf)
            Case Else
                Exit Select
        End Select
    End Sub

    Public Sub CharacterSQLEventHandler(messageId As SQL.EMessages, outBuf As String)
        Select Case messageId
            Case SQL.EMessages.ID_Error
                Log.WriteLine(LogType.FAILED, "[CHARACTER] " & outBuf)
            Case SQL.EMessages.ID_Message
                Log.WriteLine(LogType.SUCCESS, "[CHARACTER] " & outBuf)
            Case Else
                Exit Select
        End Select
    End Sub

    Public Sub WorldSQLEventHandler(messageId As SQL.EMessages, outBuf As String)
        Select Case messageId
            Case SQL.EMessages.ID_Error
                Log.WriteLine(LogType.FAILED, "[WORLD] " & outBuf)
            Case SQL.EMessages.ID_Message
                Log.WriteLine(LogType.SUCCESS, "[WORLD] " & outBuf)
            Case Else
                Exit Select
        End Select
    End Sub
#End Region

    Public Sub Start()
        Console.BackgroundColor = ConsoleColor.Black
        Dim assemblyTitleAttribute As AssemblyTitleAttribute = CType([Assembly].GetExecutingAssembly().GetCustomAttributes(GetType(AssemblyTitleAttribute), False)(0), AssemblyTitleAttribute)
        Console.Title = $"{assemblyTitleAttribute.Title } v{[Assembly].GetExecutingAssembly().GetName().Version }"

        Console.ForegroundColor = ConsoleColor.Yellow
        Dim assemblyProductAttribute As AssemblyProductAttribute = CType([Assembly].GetExecutingAssembly().GetCustomAttributes(GetType(AssemblyProductAttribute), False)(0), AssemblyProductAttribute)
        Console.WriteLine("{0}", assemblyProductAttribute.Product)

        Dim assemblyCopyrightAttribute As AssemblyCopyrightAttribute = CType([Assembly].GetExecutingAssembly().GetCustomAttributes(GetType(AssemblyCopyrightAttribute), False)(0), AssemblyCopyrightAttribute)
        Console.WriteLine(assemblyCopyrightAttribute.Copyright)

        Console.WriteLine()

        Console.ForegroundColor = ConsoleColor.Yellow

        Console.WriteLine("  __  __      _  _  ___  ___  ___   __   __ ___               ")
        Console.WriteLine(" |  \/  |__ _| \| |/ __|/ _ \/ __|  \ \ / /| _ )      We Love ")
        Console.WriteLine(" | |\/| / _` | .` | (_ | (_) \__ \   \ V / | _ \   Vanilla Wow")
        Console.WriteLine(" |_|  |_\__,_|_|\_|\___|\___/|___/    \_/  |___/              ")
        Console.WriteLine("                                                              ")
        Console.WriteLine(" Website / Forum / Support: https://getmangos.eu/             ")
        Console.WriteLine("")

        Console.ForegroundColor = ConsoleColor.Magenta

        Console.ForegroundColor = ConsoleColor.White
        Dim assemblyTitleAttribute1 As AssemblyTitleAttribute = CType([Assembly].GetExecutingAssembly().GetCustomAttributes(GetType(AssemblyTitleAttribute), False)(0), AssemblyTitleAttribute)
        Console.WriteLine(assemblyTitleAttribute1.Title)

        Console.WriteLine("version {0}", [Assembly].GetExecutingAssembly().GetName().Version)
        Console.ForegroundColor = ConsoleColor.White

        Console.WriteLine("")
        Console.ForegroundColor = ConsoleColor.Gray

        Log.WriteLine(LogType.INFORMATION, "[{0}] World Cluster Starting...", Format(TimeOfDay, "hh:mm:ss"))

        AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf GenericExceptionHandler

        LoadConfig()

        Console.ForegroundColor = ConsoleColor.Gray
        AddHandler AccountDatabase.SQLMessage, AddressOf AccountSQLEventHandler
        AddHandler CharacterDatabase.SQLMessage, AddressOf CharacterSQLEventHandler
        AddHandler WorldDatabase.SQLMessage, AddressOf WorldSQLEventHandler

        Dim ReturnValues As Integer
        ReturnValues = AccountDatabase.Connect()
        If ReturnValues > SQL.ReturnState.Success Then   'Ok, An error occurred
            Console.WriteLine("[{0}] An SQL Error has occurred", Format(TimeOfDay, "hh:mm:ss"))
            Console.WriteLine("*************************")
            Console.WriteLine("* Press any key to exit *")
            Console.WriteLine("*************************")
            Console.ReadKey()
            End
        End If
        AccountDatabase.Update("SET NAMES 'utf8';")

        ReturnValues = CharacterDatabase.Connect()
        If ReturnValues > SQL.ReturnState.Success Then   'Ok, An error occurred
            Console.WriteLine("[{0}] An SQL Error has occurred", Format(TimeOfDay, "hh:mm:ss"))
            Console.WriteLine("*************************")
            Console.WriteLine("* Press any key to exit *")
            Console.WriteLine("*************************")
            Console.ReadKey()
            End
        End If
        CharacterDatabase.Update("SET NAMES 'utf8';")

        ReturnValues = WorldDatabase.Connect()
        If ReturnValues > SQL.ReturnState.Success Then   'Ok, An error occurred
            Console.WriteLine("[{0}] An SQL Error has occurred", Format(TimeOfDay, "hh:mm:ss"))
            Console.WriteLine("*************************")
            Console.WriteLine("* Press any key to exit *")
            Console.WriteLine("*************************")
            Console.ReadKey()
            End
        End If
        WorldDatabase.Update("SET NAMES 'utf8';")

        _WS_DBCLoad.InitializeInternalDatabase()
        _WC_Handlers.IntializePacketHandlers()

        If _CommonGlobalFunctions.CheckRequiredDbVersion(AccountDatabase, ServerDb.Realm) = False Then         'Check the Database version, exit if its wrong

            If True Then
                Console.WriteLine("*************************")
                Console.WriteLine("* Press any key to exit *")
                Console.WriteLine("*************************")
                Console.ReadKey()
                End
            End If
        End If

        If _CommonGlobalFunctions.CheckRequiredDbVersion(CharacterDatabase, ServerDb.Character) = False Then         'Check the Database version, exit if its wrong

            If True Then
                Console.WriteLine("*************************")
                Console.WriteLine("* Press any key to exit *")
                Console.WriteLine("*************************")
                Console.ReadKey()
                End
            End If
        End If

        If _CommonGlobalFunctions.CheckRequiredDbVersion(WorldDatabase, ServerDb.World) = False Then         'Check the Database version, exit if its wrong

            If True Then
                Console.WriteLine("*************************")
                Console.WriteLine("* Press any key to exit *")
                Console.WriteLine("*************************")
                Console.ReadKey()
                End
            End If
        End If

        _WC_Network.WorldServer = New WC_Network.WorldServerClass()
        Dim configuration = _configurationProvider.GetConfiguration()
        server = New ProxyServer(Of WC_Network.WorldServerClass)(IPAddress.Parse(configuration.ClusterListenAddress), configuration.ClusterListenPort, _WC_Network.WorldServer)
        Log.WriteLine(LogType.INFORMATION, "Interface UP at: {0}:{1}", configuration.ClusterListenAddress, configuration.ClusterListenPort)

        GC.Collect()

        If Process.GetCurrentProcess().PriorityClass <> ProcessPriorityClass.High Then
            Log.WriteLine(LogType.WARNING, "Setting Process Priority to NORMAL..[done]")
        Else
            Log.WriteLine(LogType.WARNING, "Setting Process Priority to HIGH..[done]")
        End If

        Log.WriteLine(LogType.INFORMATION, "Load Time: {0}", Format(DateDiff(DateInterval.Second, Now, Now), "0 seconds"))
        Log.WriteLine(LogType.INFORMATION, "Used memory: {0}", Format(GC.GetTotalMemory(False), "### ### ##0 bytes"))

        WaitConsoleCommand()
    End Sub

    Public Sub WaitConsoleCommand()
        Dim tmp As String = "", CommandList() As String, cmds() As String
        Dim cmd() As String = {}
        Dim varList As Integer
        While Not _WC_Network.WorldServer.m_flagStopListen
            Try
                tmp = Log.ReadLine()
                CommandList = tmp.Split(";")

                For varList = LBound(CommandList) To UBound(CommandList)
                    cmds = Split(CommandList(varList), " ", 2)
                    If CommandList(varList).Length > 0 Then
                        '<<<<<<<<<<<COMMAND STRUCTURE>>>>>>>>>>
                        Select Case cmds(0).ToLower
                            Case "shutdown"
                                Log.WriteLine(LogType.WARNING, "Server shutting down...")
                                _WC_Network.WorldServer.m_flagStopListen = True

                            Case "info"
                                Log.WriteLine(LogType.INFORMATION, "Used memory: {0}", Format(GC.GetTotalMemory(False), "### ### ##0 bytes"))

                            Case "help"
                                Console.ForegroundColor = ConsoleColor.Blue
                                Console.WriteLine("'WorldCluster' Command list:")
                                Console.ForegroundColor = ConsoleColor.White
                                Console.WriteLine("---------------------------------")
                                Console.WriteLine("")
                                Console.WriteLine("'help' - Brings up the 'WorldCluster' Command list (this).")
                                Console.WriteLine("")
                                Console.WriteLine("'info' - Displays used memory.")
                                Console.WriteLine("")
                                Console.WriteLine("'shutdown' - Shuts down WorldCluster.")
                            Case Else
                                Console.ForegroundColor = ConsoleColor.Red
                                Console.WriteLine("Error! Cannot find specified command. Please type 'help' for information on console for commands.")
                                Console.ForegroundColor = ConsoleColor.Gray
                        End Select
                        '<<<<<<<<<<</END COMMAND STRUCTURE>>>>>>>>>>>>
                    End If
                Next
            Catch e As Exception
                Log.WriteLine(LogType.FAILED, "Error executing command [{0}]. {2}{1}", Format(TimeOfDay, "hh:mm:ss"), tmp, e.ToString, vbCrLf)
            End Try
        End While
    End Sub

    Private Sub GenericExceptionHandler(sender As Object, e As UnhandledExceptionEventArgs)
        Dim ex As Exception = e.ExceptionObject

        Log.WriteLine(LogType.CRITICAL, ex.ToString & vbCrLf)
        Log.WriteLine(LogType.FAILED, "Unexpected error has occured. An 'WorldCluster-Error-yyyy-mmm-d-h-mm.log' file has been created. Check your log folder for more information.")

        Dim tw As TextWriter
        tw = New StreamWriter(New FileStream(String.Format("WorldCluster-Error-{0}.log", Format(Now, "yyyy-MMM-d-H-mm")), FileMode.Create))
        tw.Write(ex.ToString)
        tw.Close()
    End Sub

End Class