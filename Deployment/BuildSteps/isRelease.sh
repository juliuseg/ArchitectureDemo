if [ "$IsRelease" = "true" ]; then
  echo "This is a release build, $BUILD_NUMBER"
else
  echo "This is not a release build, $BUILD_NUMBER"
fi