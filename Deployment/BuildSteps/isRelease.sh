if [ "$IsRelease" = "true" ]; then
  echo "This is a release build, %build.number%"
else
  echo "This is not a release build"
fi