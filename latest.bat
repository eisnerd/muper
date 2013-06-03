IF [%HOME%] == [] SET HOME=C:\dev\cygwin\home\Administrator
cd %HOME%\build\wpfmpdclient
git fetch origin
git checkout -b auto1 origin/master
git branch -D auto
git checkout -b auto origin/master
git branch -D auto1

SET HOST=%COMPUTERNAME%
CALL :LoCase HOST

echo %HOST%
git log|grep -q %HOST%
if errorlevel 1 git cherry-pick paths-%HOST%
C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild 
xcopy /E /C /Y /I WpfMpdClient\bin\Debug WpfMpdClient\bin\Live
cd WpfMpdClient\bin\Live\
.\WpfMpdClient.exe

GOTO:EOF

:LoCase
:: Subroutine to convert a variable VALUE to all lower case.
:: The argument for this subroutine is the variable NAME.
FOR %%i IN ("A=a" "B=b" "C=c" "D=d" "E=e" "F=f" "G=g" "H=h" "I=i" "J=j" "K=k" "L=l" "M=m" "N=n" "O=o" "P=p" "Q=q" "R=r" "S=s" "T=t" "U=u" "V=v" "W=w" "X=x" "Y=y" "Z=z") DO CALL SET "%1=%%%1:%%~i%%"
GOTO:EOF
