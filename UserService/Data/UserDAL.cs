using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using HotChocolate.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UserService.Dtos;
using UserService.Helper;
using UserService.Models;
using UserService.SyncDataServices.Http;

namespace UserService.Data
{
    public class UserDAL : IUser
    {
        private readonly AppSettings _appSettings;
        private readonly AppDbContext _dbContext;
        private readonly KafkaSettings _kafkaSettings;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IOrderDataClient _orderDataClient;

        public UserDAL(AppDbContext dbContext, UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager, IOptions<AppSettings> appSettings, IOptions<KafkaSettings> kafkaSettings, IHttpContextAccessor httpContextAccessor, IOrderDataClient orderDataClient)
        {
            _appSettings = appSettings.Value;
            _dbContext = dbContext;
            _kafkaSettings = kafkaSettings.Value;
            _roleManager = roleManager;
            _userManager = userManager;
            _httpContextAccessor = httpContextAccessor;
            _orderDataClient = orderDataClient;
        }

        public async Task AddRole(string rolename)
        {
            try
            {
                 var roleIsExist = await _roleManager.RoleExistsAsync(rolename);
                 if(roleIsExist) 
                    throw new Exception($"Role {rolename} sudah terdaftar");
                 else 
                    await _roleManager.CreateAsync(new IdentityRole(rolename));
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public async Task<User> Authenticate(string username, string password)
        {
            var userFind = await _userManager.CheckPasswordAsync(await _userManager.FindByNameAsync(username), password);
            if(!userFind)
                return null;
            var user = new User
            {
                Username = username
            };

            List<Claim> claims = new List<Claim>();
            claims.Add(new Claim(ClaimTypes.Name, user.Username));
            var result = await _userManager.FindByEmailAsync(username);
            var roles =  await _userManager.GetRolesAsync(result);

            foreach(var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_appSettings.Secret);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
                
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            user.Token = tokenHandler.WriteToken(token);
            return user;
        }

        public async Task<Order> CreateOrder(CreateOrderDto cod)
        {
            var distance = MathHelper.getDistanceFromLatLonInKm(cod.UserLatitude, cod.UserLongitude, cod.UserTargetLatitude, cod.UserTargetLongitude);
            if(distance > 100) throw new Exception("Jarak tidak boleh lebih dari 100 Km");
            var roundedDistance = MathHelper.DistanceRounding(distance);
            
            var username = _httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.Name).Value;
            if(username == null) throw new Exception("Mohon login kembali");
            var cust = await _dbContext.Customers.Where(u => u.Username == username && u.Blocked.Equals(false)).SingleOrDefaultAsync();
            var configApp = await _dbContext.ConfigApps.Where(conf => conf.Id == 1).FirstOrDefaultAsync();
            var price = roundedDistance * configApp.PricePerKM;
            var deficitAmount = MathHelper.ToRupiah(price-cust.Balance);
            if(cust.Balance<price) throw new Exception($"Mohon topup terlebih dahulu sebesar {deficitAmount}");
            try
            {
                var order = new Order()
                {
                    CustomerId = cust.Id,
                    UserLatitude = cod.UserLatitude,
                    UserLongitude = cod.UserLongitude,
                    Distance = roundedDistance,
                    Price = price,
                    Completed = false
                };
                
                var key = "order-create-" + DateTime.Now.ToString();
                var val = JObject.FromObject(order, new JsonSerializer()
                    {
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                    }).ToString(Formatting.None);
                string topic = "order-add";
                await KafkaHelper.SendKafkaAsync(_kafkaSettings, topic, key, val);
                return order;
            }
            catch (DbUpdateException ex)
            {
                throw new Exception($"Error : {ex.InnerException.Message}");
            }
        }

        public List<CreateRoleDto> GetAllRole()
        {
            List<CreateRoleDto> roles = new List<CreateRoleDto>();
            var results =  _roleManager.Roles;
            foreach(var role in results)
            {
                roles.Add(new CreateRoleDto{ RoleName = role.Name });
            }
            return roles;
        }

        public async Task<Customer> GetUserProfile()
        {
            var username = _httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.Name).Value;
            if(username == null) throw new Exception("Mohon login kembali");
            var cust = await _dbContext.Customers.Where(u => u.Username == username).SingleOrDefaultAsync();
            return cust;
        }

        public async Task Registration(CreateUserDto user)
        {
            try
            {
                var newUser = new IdentityUser
                {
                    UserName = user.Username, 
                    Email = user.Username
                };
                var result = await _userManager.CreateAsync(newUser, user.Password);
                if(!result.Succeeded)
                    throw new Exception($"Gagal menambahkan user {user.Username}. Error: {result.Errors.Select(error=>error.Description)}");

                var userResult = await _userManager.FindByNameAsync(newUser.Email);
                await _userManager.AddToRoleAsync(userResult, "User");  

                var userEntity = new Customer
                {
                    Username = user.Username,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    PhoneNumber = user.PhoneNumber,
                    Email = user.Email,
                    Balance = 0,
                    CreatedDate = DateTime.Now,
                    Blocked = false
                };

                Console.WriteLine(userEntity);

                _dbContext.Customers.Add(userEntity);
                await _dbContext.SaveChangesAsync();

                var key = "user-create-" + DateTime.Now.ToString();
                var val = JObject.FromObject(userEntity, new JsonSerializer()
                    {
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                    }).ToString(Formatting.None);
                string topic = "user-add";
                await KafkaHelper.SendKafkaAsync(_kafkaSettings, topic, key, val);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error: {ex.Message}");
            }
        }

        public async Task<Customer> TopupBalance(double amount)
        {
            if(amount <= 0 || amount > 100_000_000) throw new Exception("Value tidak boleh kurang dari 0 dan lebih dari Rp. 100.000.000,00");
            var username = _httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.Name).Value;
            Console.WriteLine(username);
            var cust = await _dbContext.Customers.Where(u => u.Username == username).SingleOrDefaultAsync();
            if(cust == null) throw new ArgumentNullException(username);
            try
            {
                cust.Balance += amount;
                await _dbContext.SaveChangesAsync();
                return cust;
            }
            catch (DbUpdateException ex)
            {
                throw new Exception($"Error: {ex.Message}");
            }
        }
    }
}