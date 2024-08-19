using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.IO;
using System.Runtime.InteropServices.JavaScript;

namespace ChatServer
{
	class Program
	{
		// Хранение информации о клиентах (сокет, IP, порт, логин)
		static Dictionary<Socket, Tuple<string, int, string>> clientInfo = new Dictionary<Socket, Tuple<string, int, string>>();
		// Хранение множества логинов для проверки уникальности
		static HashSet<string> loginSet = new HashSet<string>();
		// Объект для синхронизации доступа к разделяемым ресурсам
		static object lockObject = new object();

		static void Main(string[] args)
		{
			// Установка русской локали
			Console.OutputEncoding = Encoding.UTF8;

			// Создание серверного сокета
			Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

			// Настройка адреса сервера
			IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Any, 8080);

			try
			{
				// Привязка серверного сокета к адресу
				serverSocket.Bind(serverEndPoint);

				// Перевод серверного сокета в режим прослушивания
				serverSocket.Listen(10);

				Console.WriteLine("Сервер запущен. Ожидание подключений...");

				while (true)
				{
					// Принятие входящего подключения
					Socket clientSocket = serverSocket.Accept();

					// Создание потока для обработки клиентского подключения
					Thread clientThread = new Thread(() => ClientHandler(clientSocket));
					clientThread.Start();
				}
			}
			// Если при добавлении клиента возникла ошибка, перехватываем и выводим ее
			catch (Exception ex)
			{
				Console.WriteLine("Ошибка: " + ex.Message);
			}
			// Гарантируем закрытие сокета при завершении работы
			finally
			{
				// Закрытие серверного сокета
				serverSocket.Close();
			}
		}

		static void ClientHandler(Socket clientSocket)
		{
			// Получение информации об адресе клиента
			IPEndPoint clientEndPoint = (IPEndPoint)clientSocket.RemoteEndPoint;
			string clientIP = clientEndPoint.Address.ToString();
			int clientPort = clientEndPoint.Port;

			// Получение логина клиента
			byte[] loginBuffer = new byte[1024];
			int loginBytesRead = 0;
			string clientLogin = null;

			try
			{
				// Получение логина клиента
				loginBytesRead = clientSocket.Receive(loginBuffer);
				if (loginBytesRead > 0)
				{
					clientLogin = Encoding.UTF8.GetString(loginBuffer, 0, loginBytesRead);
				}
			}
			catch (SocketException ex)
			{
				// Обработка ошибки "Удаленный хост принудительно разорвал существующее подключение"
				if (ex.SocketErrorCode == SocketError.ConnectionReset)
				{
					Console.WriteLine("Клиент [{0}:{1}] разорвал соединение до ввода логина.", clientIP, clientPort);
				}
				else
				{
					Console.WriteLine("Ошибка: " + ex.Message);
				}
				clientSocket.Close();
				return;
			}

			// Проверка уникальности логина
			bool isLoginUnique = false;
			lock (lockObject)
			{
				if (!loginSet.Contains(clientLogin))
				{
					loginSet.Add(clientLogin);
					isLoginUnique = true;
				}
			}

			// Отправка ответа клиенту о результате проверки логина
			string loginResponse = isLoginUnique ? "OK" : "TAKEN";
			byte[] loginResponseBytes = Encoding.UTF8.GetBytes(loginResponse);
			clientSocket.Send(loginResponseBytes);

			// Если логин свободен
			if (isLoginUnique)
			{
				Console.WriteLine("Клиент подключен [{0}:{1}] (Логин: {2})", clientIP, clientPort, clientLogin);

				// Сохранение информации о клиенте (сокет, IP, порт, логин)
				lock (lockObject)
				{
					clientInfo[clientSocket] = new Tuple<string, int, string>(clientIP, clientPort, clientLogin);
				}

				// Переменная для отслеживания ошибки
				bool isExeption = false;

				try
				{
					while (true)
					{
						// Получение данных от клиента
						byte[] buffer = new byte[1024];
						int bytesRead = clientSocket.Receive(buffer);

						// Проверка на отключение клиента
						if (bytesRead == 0)
						{
							// Клиент отключился
							break;
						}

						string clientMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);

						Console.WriteLine("От клиента [{0}:{1}] (Логин: {2}) - {3}", clientIP, clientPort, clientLogin, clientMessage);

						// Выполнение команды, полученной от клиента
						string result = ExecuteCommand(clientMessage, clientSocket);

						// Проверка на команду bye для завершения работы
						if (clientMessage.ToLower() == "bye")
						{
							break;
						}

						// Отправка результата клиенту
						byte[] resultBytes = Encoding.UTF8.GetBytes(result);
						clientSocket.Send(resultBytes);
					}
				}
				// Если при работе с сетью возникла ошибка, перехватываем и выводим ее
				catch (Exception ex)
				{
					// Обработка ошибки "Удаленный хост принудительно разорвал существующее подключение"
					isExeption = true;
				}
				// Гарантируем закрытие сокета и удаление информации о клиенте
				finally
				{
					// Получение логина клиента перед удалением из словаря
					string clientLoginToRemove;
					lock (lockObject)
					{
						clientLoginToRemove = clientInfo[clientSocket].Item3;
						clientInfo.Remove(clientSocket);
					}

					// Удаление логина из множества loginSet
					lock (lockObject)
					{
						loginSet.Remove(clientLoginToRemove);
					}

					// Закрытие сокета клиента
					clientSocket.Close();
					// Проверка наличия исключения перед выводом сообщения
					if (isExeption == false)
					{
						Console.WriteLine("Клиент отключен [{0}:{1}]", clientIP, clientPort);
					}
					else
					{
						Console.WriteLine("Клиент [{0}:{1}] (логин - {2}) принудительно разорвал соединение.", clientIP, clientPort, clientLogin);
					}
				}
			}
			// Если логин занят
			else
			{
				Console.WriteLine("Клиент [{0}:{1}] отключен. Логин '{2}' уже занят.", clientIP, clientPort, clientLogin);
				clientSocket.Close();
			}
		}


		// Функция для выполнения команды, полученной от клиента
		static string ExecuteCommand(string command, Socket clientSocket)
		{
			// Разбиение команды на токены по пробелу
			string[] tokens = command.Split(' ');

			// Обработка команды в зависимости от первого токена (имени команды)
			switch (tokens[0].ToLower())
			{
				// Обработка команды help
				case "help":
					// Возврат списка доступных команд
					return "Список доступных команд:\n" +
						   "help - Вывод информации о доступных командах\n" +
						   "bye - Завершение сеанса с сервером\n" +
						   "chat <сообщение> - Отправка сообщения всем подключенным клиентам\n" +
						   "send <логин> <сообщение> - Отправка сообщения определенному клиенту\n" +
						   "who - Вывод списка всех подключенных клиентов с их IP, портом и логином\n" +
						   "ls - Вывод содержимого текущего каталога на сервере\n" +
						   "pwd - Вывод пути текущей директории на сервере\n";

				// Обработка команды chat
				case "chat":
					// Проверка наличия сообщения после команды chat
					if (tokens.Length > 1)
					{
						// Объединение оставшихся токенов в одно сообщение
						string message = string.Join(" ", tokens, 1, tokens.Length - 1);
						// Отправка сообщения всем подключенным клиентам
						BroadcastMessage(clientSocket, message);
						// Возврат сообщения об успешной отправке
						return "Сообщение отправлено всем клиентам\n";
					}
					else
					{
						// Возврат сообщения об ошибке, если отсутствует сообщение после команды chat
						return "Ошибка: некорректный формат команды chat\n";
					}

				// Обработка команды send
				case "send":
					// Проверка наличия логина и сообщения после команды send
					if (tokens.Length == 3)
					{
						// Получение логина получателя из второго токена
						string receiverLogin = tokens[1];
						// Объединение оставшихся токенов в одно сообщение
						string message = string.Join(" ", tokens, 2, tokens.Length - 2);

						// Поиск сокета получателя по логину
						Socket receiverSocket = FindClientSocketByLogin(receiverLogin);
						// Проверка наличия сокета получателя
						if (receiverSocket != null)
						{
							// Получение логина отправителя из информации о клиенте
							string senderLogin = clientInfo[clientSocket].Item3;
							// Форматирование сообщения с указанием отправителя
							string formattedMessage = string.Format("[{0}] {1}", senderLogin, message);
							// Преобразование сообщения в массив байтов
							byte[] messageBytes = Encoding.UTF8.GetBytes(formattedMessage);
							// Отправка сообщения получателю
							receiverSocket.Send(messageBytes);
							// Возврат сообщения об успешной отправке
							return "Сообщение отправлено клиенту " + receiverLogin + '\n';
						}
						else
						{
							// Возврат сообщения об ошибке, если получатель не найден
							return "Ошибка: клиент с логином " + receiverLogin + " не найден\n";
						}
					}
					else
					{
						// Возврат сообщения об ошибке, если отсутствует логин или сообщение после команды send
						return "Ошибка: некорректный формат команды send\n";
					}

				// Обработка команды who
				case "who":
					// Создание списка подключенных клиентов
					string clientList = "Список подключенных клиентов:\n";
					// Блокировка объекта синхронизации для доступа к информации о клиентах
					lock (lockObject)
					{
						// Перебор всех подключенных клиентов
						foreach (var entry in clientInfo)
						{
							// Получение IP-адреса, порта и логина клиента
							string ip = entry.Value.Item1;
							int port = entry.Value.Item2;
							string login = entry.Value.Item3;
							// Добавление информации о клиенте в список
							clientList += string.Format("IP: {0}, Порт: {1}, Логин: {2}\n", ip, port, login);
						}
					}
					// Возврат списка подключенных клиентов
					return clientList;

				// Обработка команды ls
				case "ls":
					// Получение текущего пути каталога
					string currentPath = Directory.GetCurrentDirectory();
					// Получение списка файлов в текущем каталоге
					string[] files = Directory.GetFiles(currentPath);
					// Получение списка подкаталогов в текущем каталоге
					string[] directories = Directory.GetDirectories(currentPath);

					// Создание результирующей строки с содержимым текущего каталога
					string result = "Содержимое текущего каталога:\n";
					// Добавление файлов в результирующую строку
					foreach (string file in files)
					{
						result += Path.GetFileName(file) + "\n";
					}
					// Добавление подкаталогов в результирующую строку
					foreach (string directory in directories)
					{
						result += Path.GetFileName(directory) + "\\\n";
					}
					// Возврат содержимого текущего каталога
					return result;

				// Обработка команды pwd
				case "pwd":
					// Получение текущего пути каталога
					string currentDirectory = Directory.GetCurrentDirectory();
					// Возврат текущего пути каталога
					return "Текущий путь каталога: " + currentDirectory + '\n';

				// Обработка неизвестной команды
				default:
					// Возврат сообщения о неизвестной команде
					return "Неизвестная команда\n";
			}
		}

		// Функция для отправки сообщения всем подключенным клиентам
		static void BroadcastMessage(Socket senderSocket, string message)
		{
			// Блокировка объекта синхронизации для доступа к информации о клиентах
			lock (lockObject)
			{
				// Перебор всех подключенных клиентов
				foreach (var clientSocket in clientInfo.Keys)
				{
					// Проверка, что текущий клиент не является отправителем
					if (clientSocket != senderSocket)
					{
						// Получение логина отправителя из информации о клиенте
						string senderLogin = clientInfo[senderSocket].Item3;
						// Форматирование сообщения с указанием отправителя
						string broadcastMessage = string.Format("[{0}] {1}", senderLogin, message);
						// Преобразование сообщения в массив байтов
						byte[] broadcastBytes = Encoding.UTF8.GetBytes(broadcastMessage);
						// Отправка сообщения текущему клиенту
						clientSocket.Send(broadcastBytes);
					}
				}
			}
		}

		// Функция для поиска сокета клиента по логину
		static Socket FindClientSocketByLogin(string login)
		{
			// Блокировка объекта синхронизации для доступа к информации о клиентах
			lock (lockObject)
			{
				// Перебор всех подключенных клиентов
				foreach (var entry in clientInfo)
				{
					// Проверка совпадения логина клиента с искомым логином
					if (entry.Value.Item3 == login)
					{
						// Возврат сокета клиента, если логин совпадает
						return entry.Key;
					}
				}
			}
			// Возврат null, если клиент с указанным логином не найден
			return null;
		}
	}
}
