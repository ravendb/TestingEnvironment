#!/bin/sh
rm -rf Zips >& /dev/null
mkdir Zips
for i in $(find . | grep TEOutLinux | sed 's/TEOutLinux.*/TEOutLinux/g' | sort | uniq | cut -d'/' -f2-99 | sed 's/TeAgent/TEAGENT/g' ); do tar cvfpz $(echo $i | sed 's/Agent\///g' | sed 's/Scenarios\///g' | cut -f1 -d'/' | sed 's/TEAGENT/TeAgent/g' ).tar.gz $i; done
