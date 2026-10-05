using System;
using System.Collections.Generic;

namespace OCPP.Core.Management.Models
{
    /// <summary>
    /// View model for the "Monthly Charge Report" — energy DRAWN from supply per
    /// charge point over a calendar month, using a telescoping meter-delta
    /// (includes between-session idle draw). See MonthlyChargeReportRow for the
    /// per-charge-point result and the "session active" case.
    /// </summary>
    public class MonthlyChargeReportViewModel
    {
        /// <summary>First day of the reported month (local date).</summary>
        public DateTime StartDate { get; set; }

        /// <summary>Last day of the reported month (local date).</summary>
        public DateTime StopDate { get; set; }

        /// <summary>Year of the reported month (for prev/next navigation).</summary>
        public int Year { get; set; }

        /// <summary>Month (1-12) of the reported month (for prev/next navigation).</summary>
        public int Month { get; set; }

        public List<MonthlyChargeReportRow> Rows { get; set; } = new List<MonthlyChargeReportRow>();
    }

    public class MonthlyChargeReportRow
    {
        /// <summary>Charge point display name (or id if no name).</summary>
        public string ChargePointName { get; set; }

        /// <summary>Tag that started the FIRST session of the month (name, else id).</summary>
        public string FirstTagName { get; set; }

        /// <summary>
        /// Energy drawn in kWh (telescoping meter-delta). Null when the value
        /// cannot be reliably determined — see <see cref="SessionActive"/>.
        /// </summary>
        public double? EnergyKwh { get; set; }

        /// <summary>
        /// True when the reporting month's closing anchor is an unfinished
        /// (open) session that started within the reported month — i.e. energy
        /// would be understated. The view shows "session active" instead of a number.
        /// </summary>
        public bool SessionActive { get; set; }
    }
}
