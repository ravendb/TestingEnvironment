param( $hostuser )
read-host -assecurestring | convertfrom-securestring | out-file securestring_${hostuser}.txt