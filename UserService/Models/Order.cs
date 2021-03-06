using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace UserService.Models
{
    public class Order
    {
        [Key]
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public int? DriverId { get; set; }
        public double Price { get; set; }
        public float? Distance { get; set; }
        public bool Completed { get; set; }
        public bool PickedUp { get; set; }
        public double UserLatitude { get; set; }
        public double UserLongitude { get; set; }
        public Customer Customers { get; set; }
        public Driver Driver { get; set; }
    }
}