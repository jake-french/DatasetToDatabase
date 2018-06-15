# DatasetToDatabase

C# application that converts specific (.pre) Precipitation Dataset files showing Rainfall data (1991-2000) into SQLite database.

# How-To
Clone the repository into Visual Studio and select the solution (.sln) file to load as a normal C# project. From there the solution can be built and run as a standard executable (.exe).

In order to convert a .pre file into a SQLite database (.db) the input field on the top of the application screen must match the path of the .pre file to be read. By clicking the "Select" button an open file dialog will allow for easier selection. The output text field should be automatically filled using this method, but can be edited if changes are neccessary. When both fields are filled with valid paths, clicking the "Convert" button will automate the conversion from dataset to database. The UI on screen will provide the user with feedback that the system is operating in the background and making progress.

Upon converting all data into the database a confirmation prompt will appear. In order to open the .db file to view the database contents an external program is required. Recommendation is to include the extension: https://marketplace.visualstudio.com/items?itemName=ErikEJ.SQLServerCompactSQLiteToolbox into Visual Studio to connect directly with the database file. By performing a simple SQL query: "SELECT * FROM Rainfall" should show all records the program created based off the data within the dataset.
