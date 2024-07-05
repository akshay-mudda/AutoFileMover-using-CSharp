using System;
using System.Configuration;
using System.IO;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoFileMover_using_CSharp
{
    class Program
    {
        static void Main(string[] args)
        {
            string sourceFolder = ConfigurationManager.AppSettings["SourceFolder"];
            string destinationFolder = ConfigurationManager.AppSettings["DestinationFolder"];
            string connectionString = ConfigurationManager.ConnectionStrings["FileMoveHistoryDB"].ConnectionString;

            // Check if source folder exists
            if (!Directory.Exists(sourceFolder))
            {
                Console.WriteLine($"Source folder {sourceFolder} does not exist.");
                return;
            }

            // Read file type mappings from App.config
            Dictionary<string, string> folderMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Documents", ConfigurationManager.AppSettings["DocExtensions"] },
                { "Pdf Files", ConfigurationManager.AppSettings["PdfExtensions"] },
                { "Excel Files", ConfigurationManager.AppSettings["ExcelExtensions"] },
                { "Csv Files", ConfigurationManager.AppSettings["CsvExtensions"] },
                { "Txt Files", ConfigurationManager.AppSettings["TxtExtensions"] },
                { "Images", ConfigurationManager.AppSettings["ImageExtensions"] }
            }
            .SelectMany(kvp => kvp.Value.Split(',').Select(ext => new { ext, folder = kvp.Key }))
            .ToDictionary(x => x.ext, x => x.folder, StringComparer.OrdinalIgnoreCase);

            // Ensure all necessary sub-folders exist in the destination
            foreach (var folder in folderMappings.Values.Distinct())
            {
                string subFolderPath = Path.Combine(destinationFolder, folder);
                if (!Directory.Exists(subFolderPath))
                {
                    try
                    {
                        Directory.CreateDirectory(subFolderPath);
                        Console.WriteLine($"Created folder: {subFolderPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error creating folder {subFolderPath}: {ex.Message}");
                        return;
                    }
                }
            }

            // Get all files from the source folder
            string[] files = Directory.GetFiles(sourceFolder);

            // Summary dictionary
            Dictionary<string, int> fileSummary = new Dictionary<string, int>();
            List<FileMoveRecord> moveRecords = new List<FileMoveRecord>();

            foreach (string file in files)
            {
                try
                {
                    string fileExtension = Path.GetExtension(file);
                    if (folderMappings.TryGetValue(fileExtension, out string subFolder))
                    {
                        string fileName = Path.GetFileName(file);
                        string destFile = Path.Combine(destinationFolder, subFolder, fileName);

                        // Check if the file already exists in the destination folder
                        if (File.Exists(destFile))
                        {
                            // Rename the file to avoid overwrite
                            string destFileWithoutExtension = Path.Combine(destinationFolder, subFolder, Path.GetFileNameWithoutExtension(fileName));
                            string newDestFile = $"{destFileWithoutExtension}_{DateTime.Now:yyyyMMddHHmmss}{fileExtension}";
                            File.Move(file, newDestFile);
                            Console.WriteLine($"Moved and renamed {fileName} to {Path.Combine(destinationFolder, subFolder)}");
                            moveRecords.Add(new FileMoveRecord(fileName, subFolder, sourceFolder, newDestFile));
                        }
                        else
                        {
                            File.Move(file, destFile);
                            Console.WriteLine($"Moved {fileName} to {Path.Combine(destinationFolder, subFolder)}");
                            moveRecords.Add(new FileMoveRecord(fileName, subFolder, sourceFolder, destFile));
                        }

                        // Update summary
                        if (!fileSummary.ContainsKey(subFolder))
                        {
                            fileSummary[subFolder] = 0;
                        }
                        fileSummary[subFolder]++;
                    }
                    else
                    {
                        Console.WriteLine($"No folder mapping found for file extension {fileExtension}, skipping {file}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error moving file {file}: {ex.Message}");
                }
            }

            // Insert move history into SQL table
            InsertMoveHistory(moveRecords, connectionString);

            // Print summary
            Console.WriteLine("\nMove Process Summary:");
            foreach (var entry in fileSummary)
            {
                Console.WriteLine($"{entry.Key} files moved: {entry.Value}");
            }
        }

        static void InsertMoveHistory(List<FileMoveRecord> moveRecords, string connectionString)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                foreach (var record in moveRecords)
                {
                    string query = "INSERT INTO FileMoveHistory (CreateDate, FileName, FileType, SourcePath, DestinationPath) VALUES (@CreateDate, @FileName, @FileType, @SourcePath, @DestinationPath)";
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                        command.Parameters.AddWithValue("@FileName", record.FileName);
                        command.Parameters.AddWithValue("@FileType", record.FileType);
                        command.Parameters.AddWithValue("@SourcePath", record.SourcePath);
                        command.Parameters.AddWithValue("@DestinationPath", record.DestinationPath);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }
    }

    class FileMoveRecord
    {
        public string FileName { get; }
        public string FileType { get; }
        public string SourcePath { get; }
        public string DestinationPath { get; }

        public FileMoveRecord(string fileName, string fileType, string sourcePath, string destinationPath)
        {
            FileName = fileName;
            FileType = fileType;
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
        }
    }
}
