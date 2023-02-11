use std;
use lib::add;

fn main() {
    println!("directory example: 2 + 3 = {}.", add(2, 3));

    let args: Vec<String> = std::env::args().collect();
    println!("command line: {:?}", args);

    let mut user_input = String::new();
    std::io::stdin().read_line(&mut user_input);
}
