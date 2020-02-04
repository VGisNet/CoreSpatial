﻿using System;
using System.IO;
using System.Text;
using CoreSpatial.Dbf;
using CoreSpatial.Utility;

namespace CoreSpatial.ShapeFile
{
    /// <summary>
    /// shapefile与FeatureSet转换操作
    /// </summary>
    internal class ShpManager
    {
        /// <summary>
        /// 从shapefile生成FeatureSet
        /// </summary>
        /// <param name="shpFilePath"></param>
        /// <param name="encoding"></param>
        /// <returns></returns>
        public static FeatureSet CreateFeatureSet(string shpFilePath, Encoding encoding)
        {
            if (!ShpUtil.VerificationShp(shpFilePath, out var subFiles))
            {
                throw new IOException("该shapefile文件不存在或者主文件缺失！");
            }
            //else
            var dbfReader = new DbfReader(subFiles.Item2, encoding);
            var shxIndexs = new ShxReader().ReadShx(subFiles.Item1);
            var recordNum = shxIndexs.Count;
            var shpReader = new ShpReader(shpFilePath, shxIndexs);

            var fs = CreateFeatureSet(shpReader, dbfReader, recordNum);
            
            if (File.Exists(subFiles.Item3))
            {
                string prjWkt = "";
                using (var sr = new StreamReader(subFiles.Item3, Encoding.UTF8))
                {
                    prjWkt = sr.ReadToEnd();
                }

                var proj = Crs.Crs.CreateFromWkt(prjWkt);
                fs.Crs = proj;
            }
          
            return fs;
        }

        /// <summary>
        /// 根据文件流创建shapefile
        /// </summary>
        /// <param name="shpFileStream"></param>
        /// <param name="shxFileStream"></param>
        /// <param name="dbfFileStream"></param>
        /// <param name="encoding"></param>
        /// <param name="prjFileStream"></param>
        /// <returns></returns>
        public static FeatureSet CreateFeatureSet
            (FileStream shpFileStream, FileStream shxFileStream, 
            FileStream dbfFileStream,Encoding encoding, FileStream prjFileStream =null)
        {
            
            var dbfReader = new DbfReader(dbfFileStream, encoding);
            var shxIndexs = new ShxReader().ReadShx(shxFileStream);
            var recordNum = shxIndexs.Count;
            var shpReader = new ShpReader(shpFileStream, shxIndexs);

            var fs = CreateFeatureSet(shpReader, dbfReader, recordNum);

            if (prjFileStream != null)
            {
                string prjWkt = "";
                using (var sr = new StreamReader(prjFileStream, Encoding.UTF8))
                {
                    prjWkt = sr.ReadToEnd();
                }

                var proj = Crs.Crs.CreateFromWkt(prjWkt);
                fs.Crs = proj;
            }
            return fs;
        }

        /// <summary>
        /// 根据ShpReader和DbfReader创建FeatureSet
        /// </summary>
        /// <param name="shpReader"></param>
        /// <param name="dbfReader"></param>
        /// <param name="recordNum"></param>
        /// <returns></returns>
        private static FeatureSet CreateFeatureSet(ShpReader shpReader, DbfReader dbfReader,int recordNum)
        {
            GeometryType geometryType = (GeometryType)shpReader.ShpHeader.ShapeType;
            var featureType = ShpUtil
                .GeometryType2FeatureType(geometryType);
            FeatureSet fs = new FeatureSet(featureType);
            IFeatureList features = new FeatureList(fs);
            for (int i = 0; i < recordNum; i++)
            {
                var spatialBytes = shpReader.GetNextRecord();
                dbfReader.GetNextRow();
                IGeometry geometry;
                switch (geometryType)
                {
                    case GeometryType.Point:
                        geometry = BytesToGeometry.CreatePoint(spatialBytes);
                        break;
                    case GeometryType.MultiPoint:
                        geometry = BytesToGeometry.CreateMultipoint(spatialBytes);
                        break;
                    case GeometryType.PolyLine:
                        geometry = BytesToGeometry.CreatePolyline(spatialBytes);
                        break;
                    case GeometryType.Polygon:
                        geometry = BytesToGeometry.CreatePolygon(spatialBytes);
                        break;
                    default:
                        geometry = null;
                        break;
                }
                IFeature feature = new Feature(fs)
                {
                    Geometry = geometry
                };
                features.Add(feature);
            }

            fs.Features = features;
            fs.AttrTable = dbfReader.DbfTable;
            var header = shpReader.ShpHeader;
            fs.Envelope = new Envelope(header.XMin, header.YMin,
                header.XMax, header.YMax,
                header.ZMin, header.ZMax);

            return fs;
        }

        /// <summary>
        /// 将FeatureSet保存为shapefile
        /// </summary>
        /// <param name="iFeatureSet"></param>
        /// <param name="shpFilePath"></param>
        public static void SaveFeatureSet(IFeatureSet iFeatureSet,
            string shpFilePath)
        {
            var dir = Path.GetDirectoryName(shpFilePath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var (shxFilePath, dbfFilePath, prjFilePath) = ShpUtil.GetSubFileName(shpFilePath);

            var dbfWriter = new DbfWriter(iFeatureSet.AttrTable,
                dbfFilePath
                , Encoding.GetEncoding("GB2312"));
            dbfWriter.Write();

            var featureSet = (FeatureSet)iFeatureSet;
            var header = featureSet.GetHeader();

            using ShpWriter shpWriter = new ShpWriter(shpFilePath);
            using ShxWriter shxWriter = new ShxWriter(shxFilePath);
            shpWriter.WriterHeader(header);
            shxWriter.WriterHeader(header);

            int offset = 100;
            
            foreach (var feature in featureSet.Features)
            {
                var contentByteLength = shpWriter.WriterRecord(feature);
                shxWriter.WriterRecord(offset, contentByteLength);
                
                offset += contentByteLength;
            }

            shpWriter.WriteFileLength(); ;
            shxWriter.WriteFileLength();
            if (featureSet.Crs != null)
            {
                using var fsStream = new FileStream(prjFilePath,FileMode.OpenOrCreate,FileAccess.Write,FileShare.Read);
                using var sw = new StreamWriter(fsStream);
                sw.Write(featureSet.Crs.Wkt);
            }
        }
    }
}
  