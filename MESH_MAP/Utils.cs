﻿using ArcGIS.Core.CIM;
using ArcGIS.Core.Data.UtilityNetwork.Trace;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.RightsManagement;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArcGIS.Core.Data.Raster;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Core;

namespace MESH_MAP
{
    static class Utils
    {

        public static string AddinAssemblyLocation()
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();

            return System.IO.Path.GetDirectoryName(Uri.UnescapeDataString(new Uri(asm.Location).LocalPath));
        }

        public static string GetProjectPath()
        {
            return System.IO.Path.GetDirectoryName(ArcGIS.Desktop.Core.Project.Current.URI);
        }

        public static (List<string>, List<double[]>, List<double[]>, List<double>) GetRasterData(List<RasterLayer> rLayers)
        {
            var filePaths = new List<string>();
            var coords = new List<double[]>();
            var scales = new List<double>();
            var sizes = new List<double[]>();
            //add the image file path and top left corner of each selected raster to cmd command
            foreach (var raster in rLayers)
            {
                string fullSpec = string.Empty;
                CIMDataConnection dataConnection = raster.GetDataConnection();
                if (dataConnection is CIMStandardDataConnection)
                {
                    CIMStandardDataConnection dataSConnection = dataConnection as CIMStandardDataConnection;

                    string sConnection = dataSConnection.WorkspaceConnectionString;

                    var wFactory = dataSConnection.WorkspaceFactory;
                    if (wFactory == WorkspaceFactory.Raster)
                    {
                        string sWorkspaceName = sConnection.Split('=')[1];

                        string sTable = dataSConnection.Dataset;

                        fullSpec = System.IO.Path.Combine(sWorkspaceName, sTable);
                    }
                }

                if (!string.IsNullOrEmpty(fullSpec) && !filePaths.Contains(fullSpec))
                {
                    filePaths.Add(fullSpec);

                    var extend = raster.GetRaster().GetExtent();

                    coords.Add(new double[] { extend.XMin, extend.YMax });

                    sizes.Add(new double[] { raster.GetRaster().GetWidth(), raster.GetRaster().GetHeight() });

                    scales.Add(raster.GetRaster().GetMeanCellSize().Item1);

                }
            }

            return (filePaths, coords, sizes, scales);
        }

        public static string GetRasterPath(RasterLayer rasterLayer)
        {
            string fullSpec = string.Empty;
            CIMDataConnection dataConnection = rasterLayer.GetDataConnection();
            if (dataConnection is CIMStandardDataConnection)
            {
                CIMStandardDataConnection dataSConnection = dataConnection as CIMStandardDataConnection;

                string sConnection = dataSConnection.WorkspaceConnectionString;

                var wFactory = dataSConnection.WorkspaceFactory;
                if (wFactory == WorkspaceFactory.Raster)
                {
                    string sWorkspaceName = sConnection.Split('=')[1];

                    string sTable = dataSConnection.Dataset;

                    fullSpec = System.IO.Path.Combine(sWorkspaceName, sTable);
                }
            }

            return fullSpec;
        }


        public async static Task<RasterLayer> LoadImageFile(Mat im, string name, RasterLayer parentLayer, ILayerContainerEdit groupContainer=null)
        {
            var pathProject = GetProjectPath();
            var path = pathProject + "\\" + name + ".tif";
            RasterLayer layer = null;

            await QueuedTask.Run(() =>
            {
                System.IO.File.Copy(GetRasterPath(parentLayer), path, true);

                if (groupContainer == null) groupContainer = MapView.Active.Map;

                layer = LayerFactory.Instance.CreateLayer(new Uri(path), groupContainer) as RasterLayer;

                Raster raster = layer.GetRaster();

                var pixelBlock = raster.CreatePixelBlock(raster.GetWidth(), raster.GetHeight());
                var rasterData = (float[,])pixelBlock.GetPixelData(0, false);

                for (int i = 0; i < raster.GetHeight(); i++)
                {
                    for (int j = 0; j < raster.GetWidth(); j++)
                    {
                        rasterData[j, i] = im.At<byte>(i, j);
                    }
                }

                pixelBlock.SetPixelData(0, rasterData);
                raster.Write(0, 0, pixelBlock);

                MapView.Active.Redraw(false);
            });

            return layer;
        }

        public async static void LoadShapeFiles(bool group=false)
        {
            var map = MapView.Active.Map;

            //get the newly create shape files containing the lines and polygon regions and add them to the current arcgis project
            await QueuedTask.Run(() =>
            {
                //getting directory paths
                var pathProject = ArcGIS.Desktop.Core.Project.Current.URI;
                string shapeFilesPath = System.IO.Path.GetDirectoryName(pathProject) + "\\TreeTagger";

                List<string> subDirectories = new List<string>(System.IO.Directory.GetDirectories(shapeFilesPath));
                subDirectories.RemoveAll(x => !x.Contains("Results"));
                subDirectories.Sort();
                string[] shapeFiles = System.IO.Directory.GetFiles(subDirectories[subDirectories.Count() - 1]);

                List<string> pointShapeFiles = new List<string>();
                List<string> lineShapeFiles = new List<string>();
                List<string> polygonShapeFiles = new List<string>();

                //get all not currently displayed shape files
                try
                {
                    foreach (var file in shapeFiles)
                    {

                        if (file.Contains(".shp") && !file.Contains(".lock") && !file.Contains(".xml"))
                        {
                            if (file.Contains("Point"))
                            {
                                pointShapeFiles.Add(file);
                            }
                            else if (file.Contains("Line") || file.Contains("Vector") || file.Contains("Direction"))
                            {
                                lineShapeFiles.Add(file);
                            }
                            else if (file.Contains("Outline"))
                            {
                                polygonShapeFiles.Add(file);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    MessageBox.Show(string.Format("Couldn't load Tree Tagger Shape Files from: {0}, {1}", shapeFilesPath, e.Message));
                    return;
                }

                if (pointShapeFiles.Count == 0 && lineShapeFiles.Count == 0 && polygonShapeFiles.Count == 0)
                {
                    MessageBox.Show("Couldn't find any shape files created by Tree Tagger");
                    return;
                }

                //add layers to project
                try
                {
                    ILayerContainerEdit groupContainer = group ? LayerFactory.Instance.CreateGroupLayer(map, 0, "TreeTagger") : map;

                    var pointSymbol = SymbolFactory.Instance.ConstructPointSymbol(ColorFactory.Instance.WhiteRGB, 5, SimpleMarkerStyle.Circle);

                    foreach (var sf in pointShapeFiles)
                    {
                        Uri pointFile = new(sf);
                        var pointLayer = LayerFactory.Instance.CreateLayer(pointFile, groupContainer) as FeatureLayer;

                        //Get the layer's current renderer
                        CIMSimpleRenderer renderer = pointLayer.GetRenderer() as CIMSimpleRenderer;

                        //Update the symbol of the current simple renderer
                        renderer.Symbol = pointSymbol.MakeSymbolReference();

                        //Update the feature layer renderer
                        pointLayer.SetRenderer(renderer);

                    }

                    //format line and polygon shape files to render correctly
                    var lineSymbol = SymbolFactory.Instance.ConstructLineSymbol(ColorFactory.Instance.RedRGB, 2, SimpleLineStyle.Solid);

                    foreach (var sf in lineShapeFiles)
                    {
                        Uri lineFile = new(sf);
                        var linesLayer = LayerFactory.Instance.CreateLayer(lineFile, groupContainer) as FeatureLayer;

                        //Get the layer's current renderer
                        CIMSimpleRenderer renderer = linesLayer.GetRenderer() as CIMSimpleRenderer;

                        //Update the symbol of the current simple renderer
                        renderer.Symbol = lineSymbol.MakeSymbolReference();

                        //Update the feature layer renderer
                        linesLayer.SetRenderer(renderer);
                    }

                    var outline = SymbolFactory.Instance.ConstructStroke(ColorFactory.Instance.CreateRGBColor(255, 0, 255, 30), 2.0, SimpleLineStyle.Solid);
                    var fillWithOutline = SymbolFactory.Instance.ConstructPolygonSymbol(ColorFactory.Instance.CreateRGBColor(255, 0, 255, 30), SimpleFillStyle.Solid, outline);

                    foreach (var sf in polygonShapeFiles)
                    {
                        Uri polyFile = new(sf);
                        var polygonLayer = LayerFactory.Instance.CreateLayer(polyFile, groupContainer) as FeatureLayer;

                        //Get the layer's current renderer
                        CIMSimpleRenderer rendererp = polygonLayer.GetRenderer() as CIMSimpleRenderer;

                        //Update the symbol of the current simple renderer
                        rendererp.Symbol = fillWithOutline.MakeSymbolReference();

                        //Update the feature layer renderer
                        polygonLayer.SetRenderer(rendererp);
                    }

                }
                catch (Exception e)
                {
                    MessageBox.Show(string.Format("Couldn't render shape files: {0}", e.Message));
                    return;
                }

            });


        }

        

    }
}
