set HOME=C:\dev\cygwin\home\mbrxhee2
cd %HOME%\build\wpfmpdclient
git fetch origin
git checkout -b auto1 origin/master
git branch -D auto
git checkout -b auto origin/master
git branch -D auto1
C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild 
cd WpfMpdClient\bin\Debug\
.\WpfMpdClient.exe
