using System;
using System.IO;
using System.Text;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Net;
using System.Diagnostics;

/// <summary>
/// Helper class to aid in uploading multipart
/// entities to HTTP web endpoints.
/// </summary>
class MultipartHelper
{
    private static Random random = new Random(Environment.TickCount);

    private List<NameValuePart> formData = new List<NameValuePart>();
    private FilesCollection files = null;
    private MemoryStream bufferStream = new MemoryStream();
    private string boundary;

    public String Boundary { get { return boundary; } }

    public static String GetBoundary()
    {
        return Environment.TickCount.ToString("X");
    }

    public MultipartHelper()
    {
        this.boundary = MultipartHelper.GetBoundary();
    }

    public void Add(NameValuePart part)
    {
        this.formData.Add(part);
        part.Boundary = boundary;
    }

    public void Add(FilePart part)
    {
        if (files == null)
        {
            files = new FilesCollection();
        }
        this.files.Add(part);
    }

    /// <summary>
    /// Upload using System.Net.WebClient instance
    /// </summary>
    /// <param name="client">WebClient</param>
    /// <param name="address">Url</param>
    /// <param name="method">method</param>
    public void Upload(WebClient client, string address, string method)
    {
        // set header
        client.Headers.Add("content-type", "multipart/form-data; boundary=" + this.boundary);
        Trace.WriteLine("Content-Type: multipart/form-data; boundary=" + this.boundary + "\r\n");

        // first, serialize the form data
        foreach (NameValuePart part in this.formData)
        {
            part.CopyTo(bufferStream);
        }

        // serialize the files.
        this.files.CopyTo(bufferStream);

        if (this.files.Count > 0)
        {
            // add the terminating boundary.
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("--{0}", this.Boundary).Append("\r\n");
            byte[] buffer = Encoding.ASCII.GetBytes(sb.ToString());
            bufferStream.Write(buffer, 0, buffer.Length);
        }

        bufferStream.Seek(0, SeekOrigin.Begin);

        Trace.WriteLine(Encoding.ASCII.GetString(bufferStream.ToArray()));
        byte[] response = client.UploadData(address, method, bufferStream.ToArray());
        Trace.WriteLine("----- RESPONSE ------");
        Trace.WriteLine(Encoding.ASCII.GetString(response));
    }

    /// 
    /// MimePart
    /// Abstract class for all MimeParts
    /// 
    public abstract class MimePart
    {
        public string Name { get; set; }

        public abstract string ContentDisposition { get; }

        public abstract string ContentType { get; }

        public abstract void CopyTo(Stream stream);

        public String Boundary
        {
            get;
            set;
        }
    }

    /// <summary>
    /// NameValuePart is an implementation of MimePart
    /// It supports Key/Values
    /// </summary>
    public class NameValuePart : MimePart
    {
        private Dictionary<String, String> nameValues;

        public NameValuePart(Dictionary<String, String> nameValues)
        {
            this.nameValues = nameValues;
        }

        public override void CopyTo(Stream stream)
        {
            string boundary = this.Boundary;
            StringBuilder sb = new StringBuilder();

            foreach (String element in this.nameValues.Keys)
            {
                sb.AppendFormat("--{0}", boundary);
                sb.Append("\r\n");
                sb.AppendFormat("Content-Disposition: form-data; name=\"{0}\";", element);
                sb.Append("\r\n");
                sb.Append("\r\n");
                sb.Append(this.nameValues[element.ToString()]);

                sb.Append("\r\n");

            }

            sb.AppendFormat("--{0}", boundary);
            sb.Append("\r\n");

            //Trace.WriteLine(sb.ToString());
            byte[] data = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(data, 0, data.Length);
        }

        public override string ContentDisposition
        {
            get { return "form-data"; }
        }

        public override string ContentType
        {
            get { return String.Empty; }
        }
    }

    /// <summary>
    /// FilePart - an implementation of MimePart
    /// to handle Files
    /// </summary>
    public class FilePart : MimePart
    {
        private Stream input;
        private String contentType;

        public FilePart(Stream input, String name, String contentType)
        {
            this.input = input;
            this.contentType = contentType;
            this.Name = name;
        }

        public override void CopyTo(Stream stream)
        {

            StringBuilder sb = new StringBuilder();

            sb.AppendFormat("Content-Disposition: {0}", this.ContentDisposition);

            if (this.Name != null)
                sb.Append("; ").AppendFormat("name=\"{0}\"", this.Name);

            if (this.FileName != null)
                sb.Append("; ").AppendFormat("filename=\"{0}\"", this.FileName);

            sb.Append("\r\n");

            sb.AppendFormat(this.ContentType);
            sb.Append("\r\n");
            sb.Append("\r\n");

            // serialize the header data.
            byte[] buffer = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(buffer, 0, buffer.Length);

            // send the stream.
            byte[] readBuffer = new byte[1024];
            int read = input.Read(readBuffer, 0, readBuffer.Length);

            while (read > 0)
            {
                stream.Write(readBuffer, 0, read);
                read = input.Read(readBuffer, 0, readBuffer.Length);
            }

            // write the terminating boundary
            sb.Length = 0;
            sb.Append("\r\n");
            sb.AppendFormat("--{0}", this.Boundary);

            sb.Append("\r\n");
            buffer = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(buffer, 0, buffer.Length);
        }

        public override string ContentDisposition
        {
            get { return "file"; }
        }

        public override string ContentType
        {
            get { return String.Format("content-type: {0}", this.contentType); }
        }

        public String FileName { get; set; }

    }

    /// Helper class that encapsulates all file uploads
    /// in a mime part.
    /// </summary>
    class FilesCollection : MimePart
    {
        private List<FilePart> files;

        public FilesCollection()
        {
            this.files = new List<FilePart>();
            this.Boundary = MultipartHelper.GetBoundary();
        }

        public int Count
        {
            get { return this.files.Count; }
        }

        public override string ContentDisposition
        {
            get
            {
                return String.Format("form-data; name=\"{0}\"", this.Name);
            }
        }

        public override string ContentType
        {
            get { return String.Format("multipart/mixed; boundary={0}", this.Boundary); }
        }

        public override void CopyTo(Stream stream)
        {
            // serialize the headers
            StringBuilder sb = new StringBuilder(128);
            sb.Append("Content-Disposition: ").Append(this.ContentDisposition).Append("\r\n");
            sb.Append("Content-Type: ").Append(this.ContentType).Append("\r\n");
            sb.Append("\r\n");
            sb.AppendFormat("--{0}", this.Boundary).Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            foreach (FilePart part in files)
            {
                part.Boundary = this.Boundary;
                part.CopyTo(stream);
            }
        }

        public void Add(FilePart part)
        {
            this.files.Add(part);
        }
    }
}