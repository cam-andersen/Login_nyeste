using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input; // ICommand
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;
using SystemLogin;

namespace Login;

public partial class MainWindow : Window
{
    private AccountService _accountService;
    private AppDbContext _appDbContext;
    private Account _currentUser;

    // ===== Admin tab
    public ObservableCollection<OrderViewModel> PreviousOrders { get; set; } = new();

    // ===== Create Order tab
    public ObservableCollection<ProductViewModel> AvailableProducts { get; } = new();
    public ObservableCollection<ProductViewModel> OrderLines { get; } = new();

    public string CurrentUserId => _currentUser?.Username ?? "";
    public string CurrentUserFirstName { get; set; } = "";
    public string CurrentUserLastName { get; set; } = "";

    public RelayCommand<ProductViewModel> AddToOrderCommand { get; }
    public RelayCommand<ProductViewModel> RemoveFromOrderCommand { get; }
    public RelayCommand<ProductViewModel> IncreaseQtyCommand { get; }
    public RelayCommand PlaceOrderCommand { get; }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        InitializeServices();
        Loaded += OnLoaded;

        // Commands
        AddToOrderCommand = new RelayCommand<ProductViewModel>(AddToOrder);
        RemoveFromOrderCommand = new RelayCommand<ProductViewModel>(RemoveFromOrder);
        IncreaseQtyCommand = new RelayCommand<ProductViewModel>(p => p.Quantity++);
        PlaceOrderCommand = new RelayCommand(PlaceOrder);

        // Dummy produkter
        AvailableProducts.Add(new ProductViewModel("Component A"));
        AvailableProducts.Add(new ProductViewModel("Component B"));
        AvailableProducts.Add(new ProductViewModel("Component C"));
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (await EnsureDatabaseCreatedWithExampleDataAsync())
            Log("Database did not exist. Created a new one.");
    }

    private void InitializeServices()
    {
        _appDbContext?.Dispose();
        _appDbContext = new AppDbContext();
        _accountService = new AccountService(_appDbContext, new PasswordHasher());
    }

    private async Task<bool> EnsureDatabaseCreatedWithExampleDataAsync()
    {
        var created = await _appDbContext.Database.EnsureCreatedAsync();
        if (!created) return false;

        InitializeServices();
        await _accountService.NewAccountAsync("admin", "admin", true);
        await _accountService.NewAccountAsync("user", "user");
        return true;
    }

    private async void LoginButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var username = LoginUsername.Text;
        var password = LoginPassword.Text;

        if (!await _accountService.UsernameExistsAsync(username))
        {
            Log("Username does not exist.");
            return;
        }

        if (!await _accountService.CredentialsCorrectAsync(username, password))
        {
            Log("Password wrong.");
            return;
        }

        _currentUser = await _accountService.GetAccountAsync(username);
        LogoutButton.IsVisible = true;

        Log($"{_currentUser.Username} logged in.");
        LoginUsername.Text = "";
        LoginPassword.Text = "";

        // Dummy user info til "Create Order"
        CurrentUserFirstName = "John";
        CurrentUserLastName = "Doe";

        LoadOrdersForUser(_currentUser);
    }

    private void LogoutButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _currentUser = null;
        PreviousOrders.Clear();
        LogoutButton.IsVisible = false;
        Log("Logged out.");
    }

    private void ClearLogButton_OnClick(object? sender, RoutedEventArgs e)
    {
        LogOutput.Text = "";
    }

    private async void CreateUserButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var username = CreateUserUsername.Text;
        var password = CreateUserPassword.Text;
        var isAdmin = CreateUserIsAdmin.IsChecked ?? false;

        if (await _accountService.UsernameExistsAsync(username))
        {
            Log($"Username {username} already exists.");
            return;
        }

        await _accountService.NewAccountAsync(username, password, isAdmin);
        Log($"Created user {username}.");
    }

    private async void RecreateDatabaseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _appDbContext.Database.EnsureDeletedAsync();
        await EnsureDatabaseCreatedWithExampleDataAsync();
        Log("Database recreated.");
    }

    private void OnPlaceOrderClick(object? sender, RoutedEventArgs e)
    {
        Log("Opened new order page.");
        MainTabControl.SelectedIndex = 4; // Gå til "Create Order"
    }

    private void LoadOrdersForUser(Account user)
    {
        PreviousOrders.Clear();

        var orders = _appDbContext.Orders
            .Where(o => o.AccountId == user.GetHashCode()) // Brug rigtig ID hvis muligt
            .Include(o => o.OrderLines)
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

        foreach (var order in orders)
        {
            PreviousOrders.Add(new OrderViewModel
            {
                OrderId = order.Id,
                CreatedAt = order.CreatedAt.ToShortDateString(),
                TotalQuantity = order.OrderLines.Sum(ol => ol.Quantity)
            });
        }
    }

    private void AddToOrder(ProductViewModel product)
    {
        if (product.Quantity <= 0) return;

        OrderLines.Add(new ProductViewModel(product.Name, product.Quantity));
        product.Quantity = 0;
    }

    private void RemoveFromOrder(ProductViewModel product)
    {
        OrderLines.Remove(product);
    }

    private void PlaceOrder()
    {
        Log("Order placed:");
        foreach (var line in OrderLines)
            Log($" - {line.Name} x{line.Quantity}");

        OrderLines.Clear();
    }

    private void Log(string message)
    {
        var now = DateTime.Now.ToString("yy-MM-dd HH:mm:ss");
        LogOutput.Text += $"{now} | {message}\n";
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }
}

// ViewModel for ordrer
public class OrderViewModel
{
    public int OrderId { get; set; }
    public string CreatedAt { get; set; }
    public int TotalQuantity { get; set; }
}

// ViewModel for produkter i Create Order
public class ProductViewModel
{
    public string Name { get; }
    public int Quantity { get; set; }

    public ProductViewModel(string name, int quantity = 0)
    {
        Name = name;
        Quantity = quantity;
    }
}

// RelayCommand-klasser
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();
    public void Execute(object? parameter) => _execute();
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;
    private readonly Func<T, bool>? _canExecute;

    public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _canExecute == null || parameter is T;
    public void Execute(object? parameter)
    {
        if (parameter is T value)
            _execute(value);
    }
    
    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Process order button clicked.");
    }
}
