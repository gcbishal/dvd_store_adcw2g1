﻿using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dvd_store_adcw2g1.Models
{
    public class Loan
    {
        [Key]
        [DatabaseGenerated(System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedOption.Identity)]
        public int LoanNumber { get; set; }

        public int LoanTypeNumber { get; set; }

        public int CopyNumber { get; set; }

        public int MemberNumber { get; set; }

        [ForeignKey("LoanTypeNumber")]
        public LoanType LoanType { get; set; }

        [ForeignKey("CopyNumber")]
        public DVDCopy DVDCopy { get; set; }

        [ForeignKey("MemberNumber")]
        public Member Member { get; set; }

        public DateTime DateOut { get; set; }

        public DateTime DateDue { get; set; }

        public DateTime? DateReturned { get; set; }
    }
}
