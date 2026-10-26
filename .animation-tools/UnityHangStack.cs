using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
public class UnityHangStack {
[DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint a,bool b,int p);
[DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint a,bool b,uint t);
[DllImport("kernel32.dll")] static extern uint SuspendThread(IntPtr t);
[DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr t);
[DllImport("kernel32.dll")] static extern bool GetThreadContext(IntPtr t,IntPtr c);
[DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr t);
[DllImport("dbghelp.dll",CharSet=CharSet.Ansi)] static extern bool SymInitialize(IntPtr p,string path,bool invade);
[DllImport("dbghelp.dll")] static extern bool SymCleanup(IntPtr p);
[DllImport("dbghelp.dll")] static extern uint SymSetOptions(uint o);
[DllImport("dbghelp.dll")] static extern IntPtr SymFunctionTableAccess64(IntPtr p,ulong a);
[DllImport("dbghelp.dll")] static extern ulong SymGetModuleBase64(IntPtr p,ulong a);
delegate IntPtr FunctionAccess(IntPtr p,ulong a); delegate ulong ModuleBase(IntPtr p,ulong a);
[DllImport("dbghelp.dll")] static extern bool StackWalk64(uint m,IntPtr p,IntPtr t,IntPtr f,IntPtr c,IntPtr read,FunctionAccess fa,ModuleBase mb,IntPtr tr);
[DllImport("dbghelp.dll",CharSet=CharSet.Ansi)] static extern bool SymFromAddr(IntPtr p,ulong a,out ulong d,IntPtr s);
public static void Read(int pid) {
var p=OpenProcess(0x410,false,pid); SymSetOptions(0x12); SymInitialize(p,@"D:\Unity\2022.3.62f3c1\Editor",true);
try { foreach(ProcessThread thread in Process.GetProcessById(pid).Threads) {
if(thread.ThreadState!=System.Diagnostics.ThreadState.Running && thread.Id!=Process.GetProcessById(pid).Threads[0].Id)continue;
Console.WriteLine("THREAD "+thread.Id+" "+thread.ThreadState);
var t=OpenThread(0x5a,false,(uint)thread.Id); if(t==IntPtr.Zero)continue;
var raw=Marshal.AllocHGlobal(1250);var ctx=new IntPtr((raw.ToInt64()+15)&~15L);var frame=Marshal.AllocHGlobal(1024);var sym=Marshal.AllocHGlobal(1200);
try {
for(int i=0;i<1232;i++)Marshal.WriteByte(ctx,i,0);for(int i=0;i<1024;i++)Marshal.WriteByte(frame,i,0);
Marshal.WriteInt32(ctx,48,0x10000b);
if(SuspendThread(t)==uint.MaxValue)continue;
try {
if(!GetThreadContext(t,ctx)){Console.WriteLine("context unavailable");continue;}
Marshal.WriteInt64(frame,0,Marshal.ReadInt64(ctx,248));Marshal.WriteInt32(frame,12,3);
Marshal.WriteInt64(frame,32,Marshal.ReadInt64(ctx,160));Marshal.WriteInt32(frame,44,3);
Marshal.WriteInt64(frame,48,Marshal.ReadInt64(ctx,152));Marshal.WriteInt32(frame,60,3);
for(int i=0;i<28;i++) {
ulong addr=(ulong)Marshal.ReadInt64(frame,0);if(addr==0)break;
Marshal.WriteInt32(sym,0,88);Marshal.WriteInt32(sym,80,1024);ulong displacement;
Console.WriteLine("  "+addr.ToString("x")+" "+(SymFromAddr(p,addr,out displacement,sym)?Marshal.PtrToStringAnsi(IntPtr.Add(sym,84))+"+"+displacement.ToString("x"):"unresolved"));
if(!StackWalk64(0x8664,p,t,frame,ctx,IntPtr.Zero,SymFunctionTableAccess64,SymGetModuleBase64,IntPtr.Zero))break;
}
}finally{ResumeThread(t);}
}finally{Marshal.FreeHGlobal(raw);Marshal.FreeHGlobal(frame);Marshal.FreeHGlobal(sym);CloseHandle(t);}
}} finally{SymCleanup(p);CloseHandle(p);}
}
}
