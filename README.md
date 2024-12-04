# SvgCleaner

This tool is created to solve the problems with SVG files created in FreeCAD Parts Design workbench and Laser cutting.
When using the FreeCAD SVG files directly in LaserGRBL you will end up with duplicate paths and inefficient order of paths.

This tool runs as a console application and takes the SVC-file path as commandline argument.
It will output a new SVG files without duplicate paths and minimal travel distances between paths.
