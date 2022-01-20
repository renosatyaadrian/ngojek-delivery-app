﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

#nullable disable

namespace OrderService.Models
{
    public partial class Order
    {
        [Key]
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public int? DriverId { get; set; }
        public double Price { get; set; }
        public float Distance { get; set; }
        public bool? Completed { get; set; }
        public double UserLatitude { get; set; }
        public double UserLongitude { get; set; }
        public bool? PickedUp { get; set; }

        public virtual Customer Customer { get; set; }
        public virtual Driver Driver { get; set; }
    }
}
