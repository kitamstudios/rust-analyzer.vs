use std;

fn main() {
    println!("Hello, world!");

    let args: Vec<String> = std::env::args().collect();
    println!("command line: {:?}", args);

    let mut user_input = String::new();
    std::io::stdin().read_line(&mut user_input);
}
