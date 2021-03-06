﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Mes.BE.Objects
{
 	[Serializable]
	public struct SearchProductDetailArgs
	{
		public string OrderBy;
		public int? RowNumberFrom;
		public int? RowNumberTo;

        public Guid? CategoryID;
        public Guid? ProductID;
        
        public string ProductCode;
        public string ProductName;
        public string CategoryName;
        public string Color;
        public string MaterialStyle;
        public string MaterialCategory;
        public string Size;
        public decimal? MinPrice;
        public decimal? MaxPrice;
        public string ItemName;
        public Guid? ItemID;
        public string BarcodeNo;
        public bool? IsSpecialShap;
        
	}
}
