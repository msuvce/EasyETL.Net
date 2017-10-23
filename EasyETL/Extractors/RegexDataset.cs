using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace EasyETL.DataSets
{
    public class ConditionalRegexParser
    {
        public Regex ConditionRegex = null;
        public string TableName = "";
        public Regex parseRegex = null;

    }


    public class LinesReadEventArgs : EventArgs
    {
        public int LineNumber { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    ///     Class for transforming a text-file into a dataset
    ///     based on one regular expression
    /// </summary>
    /// 
    public class RegexDataSet : DataSet
    {
        private const string NewName = "NewName";
        private const string DefaultGroup = "0";

        private Regex _contentRegex;

        protected List<RegexColumn> _regexColumns;

        public List<ConditionalRegexParser> Parsers = new List<ConditionalRegexParser>();

        public event EventHandler<LinesReadEventArgs> LineReadAndProcessed;

        // ^(?<SNo>[^,]+),(?<Name>[^,]+),(?<DateOfBirth>[^,\n]+)(?<Separator>.)(?<Occupation>(?(Separator)(.*)|()))$

        /// <summary>
        ///     Set the RegexColumnBuilder
        ///     on setting this the columns and their RegexColumnTypes are set
        ///     as is the complete ContentExpression
        /// </summary>
        public RegexColumnBuilder ColumnBuilder
        {
            set
            {
                if (value == null)
                {
                    _regexColumns = null;
                    ContentExpression = null;
                }
                else
                {
                    _regexColumns = value.Columns;
                    ContentExpression = value.CreateRegularExpression();
                }
            }
        }


        /// <summary>
        ///     Regular Expression that is used for validating textlines and
        ///     defining the column names of the dataset
        /// </summary>
        public Regex ContentExpression
        {
            get { return _contentRegex; }
            set
            {
                if ((_contentRegex == null) || !_contentRegex.ToString().Equals(value.ToString()))
                {
                    _contentRegex = value;
                    ContentExpressionHasChanged = true;
                }
            }
        }

        private bool ContentExpressionHasChanged { get; set; }

        /// <summary>
        ///     The regular expression used for handling a first row that could
        ///     contain column headers. If you do not have a first row with headers
        ///     use UseFirstRowNamesAsColumnNames=false, or if you do have a row
        ///     with headers but don't want to use them use: SkipFirstRow=true
        /// </summary>
        public Regex FirstRowExpression { get; set; }

        /// <summary>
        ///     When set to true the values in the first match made are
        ///     used as column names instead of the ones supplied in
        ///     te regular expression
        /// </summary>
        public bool UseFirstRowNamesAsColumnNames = false;

        /// <summary>
        ///     When set to true the values in the first row are
        ///     discarded.
        /// </summary>
        public bool SkipFirstRow { get; set; }

        /// <summary>
        ///     The name the datatable in the dataset should get
        ///     or the name of the datatable to use when a dataset is
        ///     provided
        /// </summary>
        public string TableName = "Table1";

        //public RegexDataSet(string fieldSeparator = ",", string tableName = "Table1", bool useFirstRowNamesAsColumnNames = true, bool skipFirstRow = false )
        //{
        //    TableName = tableName;
        //    UseFirstRowNamesAsColumnNames = useFirstRowNamesAsColumnNames;
        //    SkipFirstRow = skipFirstRow;
        //    if (useFirstRowNamesAsColumnNames)
        //    {
        //        List<string> columnNames = new List<string>();
        //        ColumnBuilder = new RegexColumnBuilder(fieldSeparator,columnNames.ToArray());
        //        FirstRowExpression = new Regex(@"([^,\n]*)[,\w]");
        //        SkipFirstRow = true;
        //    }
        //}

        public RegexDataSet(string fileName = "", string fieldSeparator = "", string tableName = "Table1", bool useFirstRowNamesAsColumns = true, bool skipFirstRow = false, params string[] columnNames)
        {

            TableName = tableName;
            UseFirstRowNamesAsColumnNames = useFirstRowNamesAsColumns;
            SkipFirstRow = skipFirstRow;
            if (useFirstRowNamesAsColumns)
            {
                if ((!String.IsNullOrWhiteSpace(fileName)) && (File.Exists(fileName)))
                {
                    using (StreamReader sr = new StreamReader(fileName))
                    {
                        string firstRow = sr.ReadLine();
                        if (String.IsNullOrWhiteSpace(fieldSeparator))
                        {
                            Regex regexSeparator = new Regex("^([a-zA-Z0-9_\"]*)(?<Separator>.)");
                            Match match = regexSeparator.Match(firstRow);
                            if (match.Success)
                            {
                                fieldSeparator = match.Groups["Separator"].ToString();
                            }
                        }
                        columnNames = firstRow.Split(fieldSeparator[0]);
                    }
                    UseFirstRowNamesAsColumnNames = false;
                }
                else
                {
                    FirstRowExpression = new Regex(@"([^" + fieldSeparator + @"\n]*)[" + fieldSeparator + @"\w]");
                }
                SkipFirstRow = true;
            }

            if (columnNames.Length > 0)
            {
                ColumnBuilder = new RegexColumnBuilder(fieldSeparator, columnNames);
                Fill(fileName);
            }
            else
            {
                ColumnBuilder = null;
            }
        }


        public RegexDataSet(RegexColumnBuilder columnBuilder)
        {
            ColumnBuilder = columnBuilder;
        }

        public RegexDataSet(params ConditionalRegexParser[] parsers)
        {
            ColumnBuilder = null;
            SkipFirstRow = false;
            UseFirstRowNamesAsColumnNames = false;
            foreach (ConditionalRegexParser crp in parsers)
            {
                Parsers.Add(crp);
            }
        }

        public RegexDataSet(XmlNode profileNode, string fileName, params ConditionalRegexParser[] parsers)
        {
            LoadProfileSettings(profileNode);
            Fill(fileName);
        }

        public void LoadProfileSettings(XmlNode xNode)
        {
            if (xNode == null) return;
            bool hasHeaderRow = false;
            bool skipFirstRow = false;
            string separator = "";
            foreach (XmlAttribute xAttr in xNode.Attributes)
            {
                switch (xAttr.Name.ToUpper())
                {
                    case "SEPARATOR":
                        separator = xAttr.Value;
                        break;
                    case "TABLENAME":
                        TableName = xAttr.Value;
                        break;
                    case "HASHEADER":
                        hasHeaderRow = Boolean.Parse(xAttr.Value);
                        break;
                    case "SKIPFIRSTROW":
                        skipFirstRow = Boolean.Parse(xAttr.Value);
                        break;
                }
            }

            UseFirstRowNamesAsColumnNames = hasHeaderRow;
            SkipFirstRow = skipFirstRow;
            RegexColumnBuilder rcb = new RegexColumnBuilder(separator);
            if (xNode.HasChildNodes)
            {
                foreach (XmlNode childNode in xNode.ChildNodes)
                {
                    ParseColumnOrParser(rcb, childNode, separator);
                }
            }
            ColumnBuilder = rcb;
        }

        private void ParseColumnOrParser(RegexColumnBuilder columnBuilder, XmlNode childNode, string separator = "")
        {
            if (childNode.Name.ToUpper() == "IF")
            {
                string strCondition = String.Empty;
                string strTableName = TableName;
                foreach (XmlAttribute xAttr in childNode.Attributes)
                {
                    switch (xAttr.Name.ToUpper())
                    {
                        case "SEPARATOR":
                            separator = xAttr.Value;
                            break;
                        case "CONDITION":
                            strCondition = xAttr.Value;
                            break;
                        case "TABLENAME":
                            strTableName = xAttr.Value;
                            break;
                    }

                }
                RegexColumnBuilder conditionalRCB = new RegexColumnBuilder(separator);
                foreach (XmlNode subNode in childNode.ChildNodes)
                {
                    ParseColumnOrParser(conditionalRCB, subNode, separator);
                }
                ConditionalRegexParser crp = new ConditionalRegexParser() { ConditionRegex = new Regex(strCondition), TableName = strTableName, parseRegex = conditionalRCB.CreateRegularExpression() };
                Parsers.Add(crp);
            }
            else
            {
                string prefix = "";
                string suffix = "";
                string strCondition = String.Empty;
                bool hasDoubleQuotes = false;
                int columnLength = 0;
                RegexColumnType rct = RegexColumnType.STRING;
                foreach (XmlAttribute xAttr in childNode.Attributes)
                {
                    switch (xAttr.Name.ToUpper())
                    {
                        case "SEPARATOR":
                            separator = xAttr.Value;
                            break;
                        case "PREFIX":
                            prefix = xAttr.Value;
                            break;
                        case "SUFFIX":
                            suffix = xAttr.Value;
                            break;
                        case "QUOTES":
                            hasDoubleQuotes = Boolean.Parse(xAttr.Value);
                            break;
                        case "LENGTH":
                            columnLength = Int16.Parse(xAttr.Value);
                            break;
                        case "TYPE":
                            rct = (RegexColumnType)Enum.Parse(typeof(RegexColumnType), xAttr.Value);
                            break;
                        case "CONDITION":
                            strCondition = xAttr.Value;
                            break;
                    }
                }
                if (!String.IsNullOrEmpty(separator))
                {
                    if (hasDoubleQuotes)
                    {
                        columnBuilder.AddColumn('\"' + childNode.Name + '\"', separator[0], rct);
                    }
                    else
                    {
                        if (childNode.NextSibling == null)
                        {
                            columnBuilder.AddColumn(childNode.Name, ".*", rct);
                        }
                        else
                        {
                            columnBuilder.AddColumn(childNode.Name, "[^" + columnBuilder.RegexFormattedOutput(separator[0]) + "\\n]*", prefix, suffix, rct);
                        }
                    }
                }
                else
                {
                    if (columnLength > 0)
                    {
                        columnBuilder.AddColumn(childNode.Name, columnLength, rct);
                    }
                    else
                    {
                        columnBuilder.AddColumn(childNode.Name, ".*", rct);
                    }
                }

                if (!String.IsNullOrWhiteSpace(strCondition))
                {
                    //There is a condition to be matched with the value... let us set it to the last column added...
                    columnBuilder.Columns[columnBuilder.Columns.Count - 1].ValueMatchingCondition = strCondition;
                }


            }
        }

        /// <summary>
        ///     The text file to be read
        /// </summary>
        public Stream TextFile { get; set; }

        private DataTable DataTable
        {
            get
            {
                if (!Tables.Contains(TableName))
                {
                    var newTable = new DataTable(TableName);
                    Tables.Add(newTable);
                }
                return Tables[TableName];
            }
        }

        /// <summary>
        ///     Lines in the text file that did not match
        ///     the regular expression
        /// </summary>
        public List<string> MisReads { get; private set; }

        private void AddMisRead(string missRead)
        {
            if (MisReads == null)
                MisReads = new List<string>();
            MisReads.Add(missRead);
        }

        private void RemoveDataTables()
        {
            while (Tables.Count > 0)
            {
                Tables.RemoveAt(0);
            }
        }

        private void BuildRegexSchemaIntoDataSet()
        {
            if ((ContentExpression == null) && !ContentExpressionHasChanged) return;
            RemoveDataTables();
            if (ContentExpression != null)
            {

                foreach (var sGroup in ContentExpression.GetGroupNames())
                {
                    short groupNum;
                    if ((sGroup != DefaultGroup) && (!Int16.TryParse(sGroup, out groupNum)))
                    {
                        var newDc = new DataColumn { DataType = typeof(string) };
                        if (_regexColumns != null)
                            foreach (var r in _regexColumns)
                                if (r.ColumnName == sGroup)
                                {
                                    newDc.DataType = r.ColumnTypeAsType;
                                    break;
                                }
                        newDc.ColumnName = sGroup;
                        DataTable.Columns.Add(newDc);
                    }
                }
            }

            foreach (ConditionalRegexParser crp in Parsers)
            {
                DataTable crpDT = null;
                if (!Tables.Contains(crp.TableName))
                {
                    crpDT = Tables.Add(crp.TableName);
                }
                else
                {
                    crpDT = Tables[crp.TableName];
                }

                foreach (var sGroup in crp.parseRegex.GetGroupNames())
                {
                    short groupNum;
                    if ((!crpDT.Columns.Contains(sGroup)) && (sGroup != DefaultGroup) && (!Int16.TryParse(sGroup, out groupNum)))
                    {
                        var newDc = new DataColumn { DataType = typeof(string) };
                        newDc.ColumnName = sGroup;
                        crpDT.Columns.Add(newDc);
                    }
                }


            }

        }

        /// <summary>
        ///     Reads every line in the text file and tries to match
        ///     it with the given regular expression.
        ///     Every match will be placed as a new row in the
        ///     datatable
        /// </summary>
        /// <param name="textFile"></param>
        /// <param name="regularExpression"></param>
        /// <param name="tableName"></param>
        public void Fill(Stream textFile, Regex regularExpression, string tableName)
        {
            TableName = tableName;
            Fill(textFile, regularExpression);
        }

        /// <summary>
        ///     Reads every line in the text file and tries to match
        ///     it with the given regular expression.
        ///     Every match will be placed as a new row in the
        ///     datatable
        /// </summary>
        /// <param name="textFile"></param>
        /// <param name="regularExpression"></param>
        public void Fill(Stream textFile, Regex regularExpression)
        {
            ContentExpression = regularExpression;
            Fill(textFile);
        }

        /// <summary>
        ///     Reads every line in the text file and tries to match
        ///     it with the given regular expression.
        ///     Every match will be placed as a new row in the
        ///     datatable
        /// </summary>
        /// <param name="textFile"></param>
        public void Fill(Stream textFile)
        {
            TextFile = textFile;
            Fill();
        }

        public virtual void Fill(string textFileName)
        {
            using (FileStream fs = new FileStream(textFileName, FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            {
                Fill(fs);
            }
        }

        /// <summary>
        ///     Reads every line in the text file and tries to match
        ///     it with the given regular expression.
        ///     Every match will be placed as a new row in the
        ///     datatable
        /// </summary>
        /// <returns></returns>
        public virtual void Fill()
        {

            if (TextFile == null)
                throw new ApplicationException("No stream available to convert to a DataSet");

            TextFile.Seek(0, SeekOrigin.Begin);
            var sr = new StreamReader(TextFile);

            var readLine = sr.ReadLine();
            var isFirstLine = true;

            int lineNumber = 1;

            SendMessageToCallingApplicationHandler(0, "Loading First Line");

            if (UseFirstRowNamesAsColumnNames && (FirstRowExpression == null) && (_regexColumns.Count == 0))
            {
                string firstRow = readLine;
                Regex regexSeparator = new Regex("^([a-zA-Z0-9_\"]*)(?<Separator>.)");
                Match match = regexSeparator.Match(firstRow);
                if (match.Success)
                {
                    string fieldSeparator = match.Groups["Separator"].ToString();
                    string[] columnNames = firstRow.Split(fieldSeparator[0]);
                    ColumnBuilder = new RegexColumnBuilder(fieldSeparator, columnNames);
                }
                UseFirstRowNamesAsColumnNames = false;
                SkipFirstRow = true;
            }


            SendMessageToCallingApplicationHandler(lineNumber, "Building Schema...");
            BuildRegexSchemaIntoDataSet();

            while (readLine != null)
            {
                if (isFirstLine && UseFirstRowNamesAsColumnNames && !SkipFirstRow)
                {
                    SendMessageToCallingApplicationHandler(lineNumber, "Building First Row...");
                    if (FirstRowExpression == null)
                        throw new RegexDataSetException(
                            "FirstRowExpression is not set, but UseFirstRowNamesAsColumnNames is set to true");
                    if (!FirstRowExpression.IsMatch(readLine))
                        throw new RegexDataSetException(
                            "The first row in the file does not match the FirstRowExpression");

                    var m = FirstRowExpression.Match(readLine);
                    foreach (var sGroup in FirstRowExpression.GetGroupNames())
                        if (sGroup != DefaultGroup)
                            DataTable.Columns[sGroup].ExtendedProperties.Add(NewName, m.Groups[sGroup].Value);
                }
                else if (!(isFirstLine && SkipFirstRow))
                {
                    ParseAndLoadLines(readLine);
                }
                SendMessageToCallingApplicationHandler(lineNumber, "Processed line");

                readLine = sr.ReadLine();
                lineNumber += 1;
                isFirstLine = false;
            }
            if (!UseFirstRowNamesAsColumnNames) return;
            foreach (DataColumn column in DataTable.Columns)
                if (column.ExtendedProperties.ContainsKey(NewName))
                    column.ColumnName = column.ExtendedProperties[NewName].ToString();
        }

        public virtual void ParseAndLoadLines(string lines)
        {
            foreach (string readLine in lines.Split(new string[] {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries))
            {
                if ((ContentExpression != null) && ContentExpression.IsMatch(readLine))
                {
                    var m = ContentExpression.Match(readLine);
                    bool bImportRow = true;
                    var newRow = DataTable.NewRow();
                    short groupNum;
                    foreach (var sGroup in ContentExpression.GetGroupNames())
                    {
                        if ((sGroup != DefaultGroup) && (!Int16.TryParse(sGroup, out groupNum)))
                        {
                            RegexColumn curRegexColumn = _regexColumns.Find(r => r.ColumnName == sGroup);
                            if (!String.IsNullOrWhiteSpace(curRegexColumn.ValueMatchingCondition) && (!Regex.IsMatch(m.Groups[sGroup].Value, curRegexColumn.ValueMatchingCondition)))
                            {
                                bImportRow = false;
                                break;
                            }
                            if (newRow.Table.Columns[sGroup].DataType == typeof(int))
                                newRow[sGroup] = Convert.ToInt32(m.Groups[sGroup].Value);
                            else if (newRow.Table.Columns[sGroup].DataType == typeof(double))
                                newRow[sGroup] = Convert.ToDouble(m.Groups[sGroup].Value);
                            else if (newRow.Table.Columns[sGroup].DataType == typeof(DateTime))
                                newRow[sGroup] = Convert.ToDateTime(m.Groups[sGroup].Value);
                            else
                                newRow[sGroup] = m.Groups[sGroup].Value;
                        }
                    }
                    if (bImportRow)
                    {
                        DataTable.Rows.Add(newRow);

                    }
                }
                else
                {
                    bool bLineParsed = false;
                    foreach (ConditionalRegexParser crp in Parsers)
                    {
                        if (crp.ConditionRegex.IsMatch(readLine))
                        {
                            DataTable crpDataTable = Tables[crp.TableName];
                            var m = crp.parseRegex.Match(readLine);
                            var newRow = crpDataTable.NewRow();
                            short groupNum;
                            foreach (var sGroup in crp.parseRegex.GetGroupNames())
                                if ((sGroup != DefaultGroup) && (!Int16.TryParse(sGroup, out groupNum)))
                                {
                                    if (newRow.Table.Columns[sGroup].DataType == typeof(int))
                                        newRow[sGroup] = Convert.ToInt32(m.Groups[sGroup].Value);
                                    else if (newRow.Table.Columns[sGroup].DataType == typeof(double))
                                        newRow[sGroup] = Convert.ToDouble(m.Groups[sGroup].Value);
                                    else if (newRow.Table.Columns[sGroup].DataType == typeof(DateTime))
                                        newRow[sGroup] = Convert.ToDateTime(m.Groups[sGroup].Value);
                                    else
                                        newRow[sGroup] = m.Groups[sGroup].Value;
                                }
                            crpDataTable.Rows.Add(newRow);
                            bLineParsed = true;
                        }
                    }
                    if (!bLineParsed)
                    {
                        AddMisRead(readLine);
                    }
                }
            }

        }

        protected void SendMessageToCallingApplicationHandler(int lineNumber, string message)
        {
            LinesReadEventArgs lrEventArgs = new LinesReadEventArgs();
            lrEventArgs.LineNumber = lineNumber;
            lrEventArgs.Message = message;
            OnLineReadAndProcessed(lrEventArgs);
        }

        protected virtual void OnLineReadAndProcessed(LinesReadEventArgs e)
        {
            EventHandler<LinesReadEventArgs> handler = LineReadAndProcessed;
            if (handler != null)
            {
                handler(this, e);
            }
        }
    }
}
