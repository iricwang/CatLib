﻿/*
 * This file is part of the CatLib package.
 *
 * (c) Yu Bin <support@catlib.io>
 *
 * For the full copyright and license information, please view the LICENSE
 * file that was distributed with this source code.
 *
 * Document: http://catlib.io/
 */
 
using System.Text;
using CatLib.API.IO;
using System.IO;

namespace CatLib.AutoUpdate{

	public class UpdateFileStore{

        [Dependency]
        public IIOFactory IO { get; set; }

        private IDisk disk;

        /// <summary>
        /// 磁盘
        /// </summary>
        private IDisk Disk{

            get{
                return disk ?? (disk = IO.Disk());
            }
        }

        public const string FILE_NAME = "update-list.catlib";

        public UpdateFile LoadFromBytes(byte[] request)
        {
            var file = new UpdateFile();
            file.Parse(new UTF8Encoding(false).GetString(request));
            return file;
        }

        public UpdateFile LoadFromPath(string path)
        {
            IFile file = Disk.File(path + Path.AltDirectorySeparatorChar + UpdateFileStore.FILE_NAME, PathTypes.Absolute);
            return LoadFromBytes(file.Read());
        }
        
        public void Save(string path , UpdateFile updateFile){

            IFile file = Disk.File(path + Path.AltDirectorySeparatorChar + UpdateFileStore.FILE_NAME, PathTypes.Absolute);
            file.Delete();
            file.Create(updateFile.Data.ToByte());

        }


	}

}
