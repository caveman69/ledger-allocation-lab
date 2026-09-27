using System;
using System.Collections.Generic;
using System.Text;

namespace LedgerAllocationLab.Data.Services;

public sealed class LedgerLabDbOptions
{
    public const string SectionName = "LedgerLabDb";

    public string ConnectionString { get; set; } = string.Empty;
}
